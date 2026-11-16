using Microsoft.EntityFrameworkCore;

namespace Marquee.Audit;

internal sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string Schema = "audit";

    public DbSet<ChangeEvent> ChangeEvents => Set<ChangeEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ChangeEvent>(change =>
        {
            change.Property(c => c.Position).HasMaxLength(200);
            change.Property(c => c.Table).HasColumnName("table_name").HasMaxLength(100);
            change.Property(c => c.Operation).HasMaxLength(20);
            change.Property(c => c.Before).HasColumnType("jsonb");
            change.Property(c => c.After).HasColumnType("jsonb");
            change.HasIndex(c => c.Position).IsUnique();
            change.HasIndex(c => new { c.Table, c.CommittedAt });
        });
    }
}
