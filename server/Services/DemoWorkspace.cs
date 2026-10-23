using System.Security.Claims;
using AccessLedger.Data;
using AccessLedger.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace AccessLedger.Services;

public static class DemoWorkspace
{
    public static async Task<Person> CreateAsync(LedgerDb db)
    {
        var now = DateTime.UtcNow;
        var workspace = new Workspace { Id = Guid.NewGuid(), CreatedAt = now, ExpiresAt = now.AddHours(24) };
        var requester = new Person { Id = Guid.NewGuid(), WorkspaceId = workspace.Id, Name = "Morgan Chen", Email = "morgan@example.test", Role = Roles.Requester };
        var approver = new Person { Id = Guid.NewGuid(), WorkspaceId = workspace.Id, Name = "Avery Brooks", Email = "avery@example.test", Role = Roles.Approver };
        var auditor = new Person { Id = Guid.NewGuid(), WorkspaceId = workspace.Id, Name = "Jordan Ellis", Email = "jordan@example.test", Role = Roles.Auditor };
        db.Workspaces.Add(workspace); db.People.AddRange(requester, approver, auditor);
        var samples = new[] {
            ("Review Project Atlas documents", "project-atlas", "Read", "I need to review the current project plans before the delivery handoff.", requester, "Pending", -3),
            ("Join the delivery workspace", "delivery-team", "Contribute", "I am coordinating the next delivery cycle and need to collaborate with the team.", requester, "Pending", -8),
            ("Read the monthly operations report", "reporting", "Read", "I need the monthly figures to prepare the operations summary.", requester, "Approved", -26),
            ("Contribute to the engineering repository", "engineering-repo", "Contribute", "I will submit a small documentation update for the next release.", requester, "Rejected", -48),
            ("Review the delivery plan", "project-atlas", "Read", "I need to check the delivery plan before tomorrow's planning session.", approver, "Pending", -2),
            ("Review operational trends", "reporting", "Read", "I need to review the fictional reporting workflow for the audit exercise.", auditor, "Approved", -36)
        };
        foreach (var (title, resource, scope, reason, person, status, hours) in samples)
        {
            var r = new AccessRequest { Id = Guid.NewGuid(), WorkspaceId = workspace.Id, RequesterId = person.Id,
                RequesterName = person.Name, Title = title, ResourceKey = resource, Scope = scope, Reason = reason,
                Status = status, Version = status == "Pending" ? 1 : 2, CreatedAt = now.AddHours(hours), UpdatedAt = now.AddHours(hours + 1) };
            db.Requests.Add(r);
            db.AuditEvents.Add(Event(r, person, "Requested", reason, r.CreatedAt));
            if (status != "Pending") db.AuditEvents.Add(Event(r, approver, status,
                status == "Approved" ? "Read-only access matches the documented business need." : "Please request read access first; contribution is not needed for this task.", r.UpdatedAt));
        }
        await db.SaveChangesAsync();
        return requester;
    }
    public static AuditEvent Event(AccessRequest r, Person actor, string action, string summary, DateTime? at = null) => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = r.WorkspaceId, RequestId = r.Id, ActorName = actor.Name,
        ActorRole = actor.Role, Action = action, Summary = summary, OccurredAt = at ?? DateTime.UtcNow
    };
    public static async Task SignInAsync(HttpContext context, Person person)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, person.Id.ToString()), new Claim(ClaimTypes.Name, person.Name),
            new Claim(ClaimTypes.Email, person.Email), new Claim(ClaimTypes.Role, person.Role), new Claim("workspace", person.WorkspaceId.ToString()) };
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12) });
    }
}
public sealed class WorkspaceCleanup(IServiceScopeFactory scopeFactory, ILogger<WorkspaceCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LedgerDb>();
                await db.Workspaces.Where(w => w.ExpiresAt < DateTime.UtcNow).ExecuteDeleteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogWarning(e, "Workspace cleanup did not complete."); }
        }
    }
}
