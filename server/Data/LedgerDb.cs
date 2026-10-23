using AccessLedger.Domain;
using Microsoft.EntityFrameworkCore;

namespace AccessLedger.Data;

public class LedgerDb(DbContextOptions<LedgerDb> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<AccessRequest> Requests => Set<AccessRequest>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Person>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<AccessRequest>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<AuditEvent>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<AccessRequest>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<AccessRequest>().HasIndex(x => new { x.WorkspaceId, x.Status, x.CreatedAt });
        model.Entity<AuditEvent>().HasIndex(x => new { x.WorkspaceId, x.RequestId, x.OccurredAt });
        model.Entity<Workspace>().HasIndex(x => x.ExpiresAt);
        model.Entity<Person>().HasIndex(x => new { x.WorkspaceId, x.Role }).IsUnique();
        model.Entity<Person>().Property(x => x.Name).HasMaxLength(100);
        model.Entity<AccessRequest>().Property(x => x.Title).HasMaxLength(100);
        model.Entity<AccessRequest>().Property(x => x.Reason).HasMaxLength(2000);
        model.Entity<AuditEvent>().Property(x => x.Summary).HasMaxLength(2000);
    }
}
