using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AccessLedger.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccessLedger.Tests;

public sealed class TestApp : WebApplicationFactory<Program>
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "accessledger-test-" + Guid.NewGuid());
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing").UseSetting("DataDirectory", directory);
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // SQLite's connection pool may retain handles until the process ends; the OS temp cleaner owns test files.
    }
}
public class WorkflowTests
{
    private static async Task<JsonNode> Get(HttpClient c, string path)
    {
        var response = await c.GetAsync(path); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }
    private static async Task<HttpResponseMessage> Post(HttpClient c, string path, object body)
    {
        var session = await Get(c, "/api/session");
        var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        message.Headers.Add("X-CSRF-TOKEN", session["csrfToken"]!.GetValue<string>());
        return await c.SendAsync(message);
    }
    private static async Task Start(HttpClient c)
    {
        var response = await Post(c, "/api/demo/start", new { }); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    private static async Task Role(HttpClient c, string role)
    {
        var response = await Post(c, "/api/demo/persona", new { role }); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    private static object NewRequest(string title = "Review the operations report") => new { title, resourceKey = "reporting", scope = "Read", reason = "I need to review the report for our next delivery planning session." };
    private static async Task<JsonNode> Create(HttpClient c)
    {
        var response = await Post(c, "/api/requests", NewRequest()); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    [Fact] public async Task Anonymous_access_and_forged_forms_are_rejected()
    {
        using var app = new TestApp(); using var client = app.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/requests")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/demo/start", new { })).StatusCode);
        var r = new HttpRequestMessage(HttpMethod.Post, "/api/demo/start") { Content = JsonContent.Create(new { }) }; r.Headers.Add("X-CSRF-TOKEN", "forged");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(r)).StatusCode);
    }
    [Fact] public async Task Browser_workspaces_do_not_share_records_even_with_a_more_privileged_role()
    {
        using var app = new TestApp(); using var first = app.Browser(); using var second = app.Browser(); await Start(first); await Start(second);
        var request = await Create(first); var id = request["id"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync("/api/requests/" + id)).StatusCode);
        await Role(second, "Approver");
        Assert.Equal(HttpStatusCode.NotFound, (await Post(second, "/api/requests/" + id + "/decision", new { decision = "Approved", note = "The request has a clear business justification.", expectedVersion = 1 })).StatusCode);
        var audit = await Get(second, "/api/audit"); Assert.DoesNotContain(audit["events"]!.AsArray(), x => x!["requestId"]!.GetValue<string>() == id);
    }
    [Fact] public async Task Requesters_cannot_decide_and_auditors_cannot_write()
    {
        using var app = new TestApp(); using var client = app.Browser(); await Start(client); var row = await Create(client); var id = row["id"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "/api/requests/" + id + "/decision", new { decision = "Approved", note = "A clear business need has been recorded.", expectedVersion = 1 })).StatusCode);
        await Role(client, "Auditor");
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "/api/requests", NewRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "/api/requests/" + id + "/cancel", new { expectedVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/requests/" + id)).StatusCode);
    }
    [Theory] [InlineData("Approved")] [InlineData("Rejected")]
    public async Task Decisions_persist_state_and_their_matching_audit_event(string decision)
    {
        using var app = new TestApp(); using var client = app.Browser(); await Start(client); var row = await Create(client); var id = row["id"]!.GetValue<string>(); await Role(client, "Approver");
        var result = await Post(client, "/api/requests/" + id + "/decision", new { decision, note = "This is a documented independent decision.", expectedVersion = 1 }); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var detail = await Get(client, "/api/requests/" + id);
        Assert.Equal(decision, detail["request"]!["status"]!.GetValue<string>()); Assert.Equal(2, detail["request"]!["version"]!.GetValue<int>());
        var events = detail["events"]!.AsArray(); Assert.Equal(2, events.Count); Assert.Equal(decision, events.Last()!["action"]!.GetValue<string>());
        Assert.Equal("Avery Brooks", events.Last()!["actorName"]!.GetValue<string>());
    }
    [Fact] public async Task Approvers_cannot_approve_their_own_requests()
    {
        using var app = new TestApp(); using var client = app.Browser(); await Start(client); await Role(client, "Approver"); var row = await Create(client); var id = row["id"]!.GetValue<string>();
        var result = await Post(client, "/api/requests/" + id + "/decision", new { decision = "Approved", note = "This attempt must be blocked by the API.", expectedVersion = 1 }); Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        var detail = await Get(client, "/api/requests/" + id); Assert.Equal("Pending", detail["request"]!["status"]!.GetValue<string>()); Assert.Single(detail["events"]!.AsArray());
    }
    [Fact] public async Task Stale_decisions_cannot_overwrite_newer_state_or_append_false_audit_events()
    {
        using var app = new TestApp(); using var client = app.Browser(); await Start(client); var row = await Create(client); var id = row["id"]!.GetValue<string>(); await Role(client, "Approver");
        Assert.Equal(HttpStatusCode.Conflict, (await Post(client, "/api/requests/" + id + "/decision", new { decision = "Approved", note = "This version is intentionally incorrect.", expectedVersion = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/api/requests/" + id + "/decision", new { decision = "Approved", note = "The documented business need is sufficient.", expectedVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(client, "/api/requests/" + id + "/decision", new { decision = "Rejected", note = "This stale decision must not be recorded.", expectedVersion = 1 })).StatusCode);
        var detail = await Get(client, "/api/requests/" + id); Assert.Equal("Approved", detail["request"]!["status"]!.GetValue<string>()); Assert.Equal(2, detail["events"]!.AsArray().Count);
    }
    [Fact] public async Task Simultaneous_reviews_produce_one_decision_and_one_conflict()
    {
        using var app = new TestApp(); using var client = app.Browser(); await Start(client); var row = await Create(client); var id = row["id"]!.GetValue<string>(); await Role(client, "Approver"); var token = (await Get(client, "/api/session"))["csrfToken"]!.GetValue<string>();
        HttpRequestMessage Message(string decision)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, "/api/requests/" + id + "/decision") { Content = JsonContent.Create(new { decision, note = "An independent review with a documented reason.", expectedVersion = 1 }) };
            message.Headers.Add("X-CSRF-TOKEN", token); return message;
        }
        var responses = await Task.WhenAll(client.SendAsync(Message("Approved")), client.SendAsync(Message("Rejected")));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var detail = await Get(client, "/api/requests/" + id); Assert.Equal(2, detail["events"]!.AsArray().Count);
    }
    [Fact] public async Task Withdrawal_preserves_history_and_prevents_later_decisions()
    {
        using var app = new TestApp(); using var client = app.Browser(); await Start(client); var row = await Create(client); var id = row["id"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/api/requests/" + id + "/cancel", new { expectedVersion = 1 })).StatusCode);
        await Role(client, "Approver"); Assert.Equal(HttpStatusCode.Conflict, (await Post(client, "/api/requests/" + id + "/decision", new { decision = "Approved", note = "A withdrawn request cannot be approved.", expectedVersion = 2 })).StatusCode);
        var detail = await Get(client, "/api/requests/" + id); Assert.Equal("Cancelled", detail["request"]!["status"]!.GetValue<string>()); Assert.Equal(2, detail["events"]!.AsArray().Count);
    }
    [Fact] public async Task Invalid_inputs_do_not_create_requests_and_caller_supplied_ownership_is_ignored()
    {
        using var app = new TestApp(); using var client = app.Browser(); await Start(client); var before = await Get(client, "/api/requests");
        var bad = await Post(client, "/api/requests", new { title = "x", resourceKey = "secret-resource", scope = "Admin", reason = "x" }); Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(before["stats"]!["total"]!.GetValue<int>(), (await Get(client, "/api/requests"))["stats"]!["total"]!.GetValue<int>());
        var session = await Get(client, "/api/session"); var response = await Post(client, "/api/requests", new { title = "Review the delivery plan", resourceKey = "project-atlas", scope = "Read", reason = "This request has a valid fictional business reason.", workspaceId = Guid.NewGuid(), requesterId = Guid.NewGuid(), requesterName = "Forged owner", status = "Approved" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); var created = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(session["user"]!["id"]!.GetValue<string>(), created["requesterId"]!.GetValue<string>()); Assert.Equal("Pending", created["status"]!.GetValue<string>());
    }
    [Fact] public async Task Expired_workspaces_lose_access_without_affecting_other_visitors()
    {
        using var app = new TestApp(); using var client = app.Browser(); using var other = app.Browser(); await Start(client); await Start(other); var session = await Get(client, "/api/session"); var personId = Guid.Parse(session["user"]!["id"]!.GetValue<string>());
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LedgerDb>(); var person = await db.People.SingleAsync(p => p.Id == personId); var workspace = await db.Workspaces.SingleAsync(w => w.Id == person.WorkspaceId); workspace.ExpiresAt = DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/requests")).StatusCode); Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/requests")).StatusCode);
    }
}
