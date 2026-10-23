namespace AccessLedger.Domain;

public static class Roles
{
    public const string Requester = "Requester", Approver = "Approver", Auditor = "Auditor";
    public static readonly string[] All = [Requester, Approver, Auditor];
}
public class Workspace
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
public class Person
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
}
public class AccessRequest
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid RequesterId { get; set; }
    public string RequesterName { get; set; } = "";
    public string Title { get; set; } = "";
    public string ResourceKey { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
public class AuditEvent
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid RequestId { get; set; }
    public string ActorName { get; set; } = "";
    public string ActorRole { get; set; } = "";
    public string Action { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime OccurredAt { get; set; }
}
public record Resource(string Key, string Name, string System, string Description);
public static class Catalog
{
    public static readonly Resource[] Resources = [
        new("project-atlas", "Project Atlas", "SharePoint", "Project plans and working documents"),
        new("delivery-team", "Delivery team", "Teams", "A fictional cross-team collaboration space"),
        new("engineering-repo", "Engineering workspace", "Azure DevOps", "A sample application repository"),
        new("reporting", "Operations reports", "Analytics", "Fictional operational reporting")
    ];
    public static readonly string[] Scopes = ["Read", "Contribute"];
    public static string Name(string key) => Resources.First(x => x.Key == key).Name;
}
public record CreateRequestInput(string Title, string ResourceKey, string Scope, string Reason);
public record DecisionInput(string Decision, string Note, int ExpectedVersion);
public record CancelInput(int ExpectedVersion);
public record PersonaInput(string Role);
public record RequestView(Guid Id, string Title, string ResourceKey, string ResourceName, string System, string Scope,
    string Reason, Guid RequesterId, string RequesterName, string Status, int Version, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static RequestView From(AccessRequest r) => new(r.Id, r.Title, r.ResourceKey, Catalog.Name(r.ResourceKey),
        Catalog.Resources.First(x => x.Key == r.ResourceKey).System, r.Scope, r.Reason, r.RequesterId, r.RequesterName,
        r.Status, r.Version, r.CreatedAt, r.UpdatedAt);
}
