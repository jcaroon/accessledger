using System.Security.Claims;
using System.Threading.RateLimiting;
using AccessLedger.Data;
using AccessLedger.Domain;
using AccessLedger.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 16 * 1024);
var dataDir = builder.Configuration["DataDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDir);
builder.Services.AddDbContext<LedgerDb>(o => o.UseSqlite($"Data Source={Path.Combine(dataDir, "ledger.db")};Foreign Keys=True;Default Timeout=10"));
builder.Services.AddDataProtection().SetApplicationName("AccessLedger").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = builder.Environment.IsDevelopment() ? "accessledger.session" : "__Host-accessledger.session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.Name = builder.Environment.IsDevelopment() ? "accessledger.antiforgery" : "__Host-accessledger.antiforgery";
    o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.Configure<ForwardedHeadersOptions>(o => o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddFixedWindowLimiter("demo-start", p => { p.PermitLimit = builder.Environment.IsEnvironment("Testing") ? 1000 : 10; p.Window = TimeSpan.FromMinutes(1); p.QueueLimit = 0; });
});
builder.Services.AddOpenApi();
builder.Services.AddHostedService<WorkspaceCleanup>();
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LedgerDb>();
    await db.Database.MigrateAsync();
}
app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    try { await next(context); }
    catch (BadHttpRequestException) { await Results.Problem("Check the request format and try again.", statusCode: 400, title: "Invalid request").ExecuteAsync(context); }
    catch (DbUpdateConcurrencyException) { await Results.Problem("This request changed while you were reviewing it. Refresh and review the latest version.", statusCode: 409, title: "Request changed").ExecuteAsync(context); }
    catch (Exception e) when (e is not OperationCanceledException)
    {
        app.Logger.LogError(e, "An application request failed.");
        if (!context.Response.HasStarted) await Results.Problem("Please try again. If the problem continues, start a fresh demo workspace.", statusCode: 500, title: "Something went wrong").ExecuteAsync(context);
    }
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true && context.Request.Path.StartsWithSegments("/api"))
    {
        var db = context.RequestServices.GetRequiredService<LedgerDb>();
        if (!Guid.TryParse(context.User.FindFirstValue("workspace"), out var id) || !await db.Workspaces.AnyAsync(w => w.Id == id && w.ExpiresAt > DateTime.UtcNow))
        { await context.SignOutAsync(); context.Response.StatusCode = 401; return; }
    }
    if (context.Request.Path.StartsWithSegments("/api") && HttpMethods.IsPost(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { await Results.Problem("Reload the page and try again.", statusCode: 400, title: "Form verification failed").ExecuteAsync(context); return; }
    }
    await next(context);
});
app.MapGet("/api/health", async (LedgerDb db) => Results.Ok(new { status = await db.Database.CanConnectAsync() ? "healthy" : "unavailable" }));
app.MapGet("/api/session", (HttpContext c, IAntiforgery antiforgery) => Results.Ok(new
{
    authenticated = c.User.Identity?.IsAuthenticated == true,
    user = c.User.Identity?.IsAuthenticated == true ? new { id = c.User.FindFirstValue(ClaimTypes.NameIdentifier), name = c.User.Identity.Name, role = c.User.FindFirstValue(ClaimTypes.Role), email = c.User.FindFirstValue(ClaimTypes.Email) } : null,
    csrfToken = antiforgery.GetAndStoreTokens(c).RequestToken,
    demo = true
}));
app.MapPost("/api/demo/start", async (LedgerDb db, HttpContext c) =>
{
    if (await db.Workspaces.CountAsync(w => w.ExpiresAt > DateTime.UtcNow) >= 1000)
        return Results.Problem("The demo is busy. Please try again later.", statusCode: 503);
    var person = await DemoWorkspace.CreateAsync(db); await DemoWorkspace.SignInAsync(c, person);
    return Results.Ok(new { message = "Your private sample workspace is ready." });
}).RequireRateLimiting("demo-start");
app.MapPost("/api/demo/persona", async (PersonaInput input, LedgerDb db, HttpContext c) =>
{
    if (!Roles.All.Contains(input.Role)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Choose a demo persona."] });
    var workspace = WorkspaceId(c); var person = await db.People.SingleAsync(p => p.WorkspaceId == workspace && p.Role == input.Role);
    await DemoWorkspace.SignInAsync(c, person); return Results.Ok(new { message = "Demo persona changed." });
}).RequireAuthorization();
app.MapGet("/api/resources", () => Results.Ok(new { resources = Catalog.Resources, scopes = Catalog.Scopes })).RequireAuthorization();
app.MapGet("/api/requests", async (string? search, string? status, LedgerDb db, HttpContext c) =>
{
    var visible = VisibleRequests(db, c);
    var stats = new { total = await visible.CountAsync(), pending = await visible.CountAsync(x => x.Status == "Pending"), approved = await visible.CountAsync(x => x.Status == "Approved"), rejected = await visible.CountAsync(x => x.Status == "Rejected") };
    var filtered = visible;
    if (!string.IsNullOrWhiteSpace(search))
    {
        if (search.Length > 100) return Results.Problem("Search is limited to 100 characters.", statusCode: 400);
        var term = search.Trim().ToLowerInvariant();
        filtered = filtered.Where(x => x.Title.ToLower().Contains(term) || x.RequesterName.ToLower().Contains(term));
    }
    if (!string.IsNullOrWhiteSpace(status) && status != "All")
    {
        if (!new[] { "Pending", "Approved", "Rejected", "Cancelled" }.Contains(status)) return Results.Problem("Choose a valid status.", statusCode: 400);
        filtered = filtered.Where(x => x.Status == status);
    }
    var rows = await filtered.OrderByDescending(x => x.CreatedAt).Take(100).AsNoTracking().ToListAsync();
    return Results.Ok(new { items = rows.Select(RequestView.From), stats });
}).RequireAuthorization();
app.MapGet("/api/requests/{id:guid}", async (Guid id, LedgerDb db, HttpContext c) =>
{
    var row = await VisibleRequests(db, c).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
    if (row is null) return Results.NotFound();
    var workspace = WorkspaceId(c);
    var events = await db.AuditEvents.Where(x => x.WorkspaceId == workspace && x.RequestId == id).OrderBy(x => x.OccurredAt).AsNoTracking().ToListAsync();
    return Results.Ok(new { request = RequestView.From(row), events });
}).RequireAuthorization();
app.MapPost("/api/requests", async (CreateRequestInput input, LedgerDb db, HttpContext c) =>
{
    if (c.User.IsInRole(Roles.Auditor)) return Results.Forbid();
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length is < 6 or > 100) errors["title"] = ["Use a title between 6 and 100 characters."];
    if (!Catalog.Resources.Any(x => x.Key == input.ResourceKey)) errors["resourceKey"] = ["Choose an available resource."];
    if (!Catalog.Scopes.Contains(input.Scope)) errors["scope"] = ["Choose Read or Contribute access."];
    if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length is < 15 or > 2000) errors["reason"] = ["Explain the business need in 15–2,000 characters."];
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var workspace = WorkspaceId(c);
    if (await db.Requests.CountAsync(x => x.WorkspaceId == workspace) >= 50) return Results.Problem("This sample workspace has reached 50 requests. Start a fresh demo to continue.", statusCode: 409);
    var person = await CurrentPerson(db, c); var now = DateTime.UtcNow;
    var row = new AccessRequest { Id = Guid.NewGuid(), WorkspaceId = workspace, RequesterId = person.Id, RequesterName = person.Name,
        Title = input.Title.Trim(), ResourceKey = input.ResourceKey, Scope = input.Scope, Reason = input.Reason.Trim(), CreatedAt = now, UpdatedAt = now };
    db.Requests.Add(row); db.AuditEvents.Add(DemoWorkspace.Event(row, person, "Requested", row.Reason)); await db.SaveChangesAsync();
    return Results.Created($"/api/requests/{row.Id}", RequestView.From(row));
}).RequireAuthorization();
app.MapPost("/api/requests/{id:guid}/decision", async (Guid id, DecisionInput input, LedgerDb db, HttpContext c) =>
{
    if (!c.User.IsInRole(Roles.Approver)) return Results.Forbid();
    if (!new[] { "Approved", "Rejected" }.Contains(input.Decision)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["decision"] = ["Choose Approve or Reject."] });
    if (string.IsNullOrWhiteSpace(input.Note) || input.Note.Trim().Length is < 10 or > 2000) return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = ["Give a decision note between 10 and 2,000 characters."] });
    var workspace = WorkspaceId(c); var row = await db.Requests.SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.Id == id);
    if (row is null) return Results.NotFound();
    if (row.RequesterId == PersonId(c)) return Results.Problem("An approver cannot decide their own request.", statusCode: 403, title: "Separate approval required");
    if (row.Version != input.ExpectedVersion || row.Status != "Pending") return Conflict();
    row.Status = input.Decision; row.Version++; row.UpdatedAt = DateTime.UtcNow;
    db.AuditEvents.Add(DemoWorkspace.Event(row, await CurrentPerson(db, c), input.Decision, input.Note.Trim())); await db.SaveChangesAsync();
    return Results.Ok(RequestView.From(row));
}).RequireAuthorization();
app.MapPost("/api/requests/{id:guid}/cancel", async (Guid id, CancelInput input, LedgerDb db, HttpContext c) =>
{
    if (c.User.IsInRole(Roles.Auditor)) return Results.Forbid();
    var workspace = WorkspaceId(c); var personId = PersonId(c); var row = await db.Requests.SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.Id == id && x.RequesterId == personId);
    if (row is null) return Results.NotFound();
    if (row.Status != "Pending" || row.Version != input.ExpectedVersion) return Conflict();
    row.Status = "Cancelled"; row.Version++; row.UpdatedAt = DateTime.UtcNow;
    db.AuditEvents.Add(DemoWorkspace.Event(row, await CurrentPerson(db, c), "Cancelled", "The requester withdrew this request.")); await db.SaveChangesAsync();
    return Results.Ok(RequestView.From(row));
}).RequireAuthorization();
app.MapGet("/api/audit", async (LedgerDb db, HttpContext c) =>
{
    var workspace = WorkspaceId(c); var ids = VisibleRequests(db, c).Select(x => x.Id);
    var rows = await db.AuditEvents.Where(x => x.WorkspaceId == workspace && ids.Contains(x.RequestId)).OrderByDescending(x => x.OccurredAt).Take(200).AsNoTracking().ToListAsync();
    return Results.Ok(new { events = rows });
}).RequireAuthorization();
app.MapOpenApi();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = 404; return; }
    var index = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
    if (File.Exists(index)) await context.Response.SendFileAsync(index); else context.Response.StatusCode = 404;
});
app.Run();
static Guid WorkspaceId(HttpContext c) => Guid.Parse(c.User.FindFirstValue("workspace")!);
static Guid PersonId(HttpContext c) => Guid.Parse(c.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
static IQueryable<AccessRequest> VisibleRequests(LedgerDb db, HttpContext c)
{
    var workspace = WorkspaceId(c); var query = db.Requests.Where(x => x.WorkspaceId == workspace);
    var personId = PersonId(c);
    return c.User.IsInRole(Roles.Requester) ? query.Where(x => x.RequesterId == personId) : query;
}
static Task<Person> CurrentPerson(LedgerDb db, HttpContext c)
{
    var workspace = WorkspaceId(c); var id = PersonId(c); return db.People.SingleAsync(x => x.WorkspaceId == workspace && x.Id == id);
}
static IResult Conflict() => Results.Problem("This request has changed or is no longer pending. Refresh before making another decision.", statusCode: 409, title: "Request changed");
public partial class Program { }
