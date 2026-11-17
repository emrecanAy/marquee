using Marquee.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Identity.Persistence;

internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string Schema = "identity";

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<SessionToken> SessionTokens => Set<SessionToken>();

    public DbSet<SignInThrottle> SignInThrottles => Set<SignInThrottle>();

    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Account>(account =>
        {
            account.Property(a => a.Id).ValueGeneratedNever();
            account.Property(a => a.Email).HasMaxLength(200);
            account.Property(a => a.FullName).HasMaxLength(120);
            account.Property(a => a.Role).HasMaxLength(20);
            account.Property(a => a.TotpSecret).HasMaxLength(200);
            account.Property(a => a.PendingTotpSecret).HasMaxLength(200);
            account.Ignore(a => a.TwoStepEnabled);
            account.Ignore(a => a.IsStaff);
            account.HasIndex(a => a.Email).IsUnique();
        });

        modelBuilder.Entity<SessionToken>(token =>
        {
            token.Property(t => t.Id).ValueGeneratedNever();
            token.Property(t => t.TokenHash).HasMaxLength(64);
            token.Property(t => t.Device).HasMaxLength(SessionToken.MaxDeviceLength);
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.HasIndex(t => t.FamilyId);
            token.HasIndex(t => t.AccountId);
            token.HasOne<Account>().WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SignInThrottle>(throttle =>
        {
            throttle.HasKey(t => t.Email);
            throttle.Property(t => t.Email).HasMaxLength(200);
        });

        modelBuilder.Entity<RecoveryCode>(code =>
        {
            code.Property(c => c.Id).ValueGeneratedNever();
            code.Property(c => c.CodeHash).HasMaxLength(64);
            code.HasIndex(c => new { c.AccountId, c.CodeHash });
            code.HasOne<Account>().WithMany().HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
