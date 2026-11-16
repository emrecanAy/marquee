using Marquee.Common.Messaging;
using Marquee.Payments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Payments.Persistence;

internal sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public const string Schema = "payments";

    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema).AddOutbox();

        modelBuilder.Entity<Payment>(payment =>
        {
            payment.Property(p => p.Id).ValueGeneratedNever();
            payment.Property(p => p.IdempotencyKey).HasMaxLength(100);
            payment.Property(p => p.Amount).HasPrecision(10, 2);
            payment.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            payment.Property(p => p.FailureReason).HasMaxLength(200);
            payment.Property(p => p.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();

            // A retried request finds its first attempt instead of creating a second payment.
            payment.HasIndex(p => new { p.SessionId, p.IdempotencyKey }).IsUnique();
            // Even with different keys, a reservation can have only one payment that is in flight or
            // completed. Failed attempts do not count, so the customer can try another card.
            payment.HasIndex(p => p.ReservationId).IsUnique().HasFilter("status IN ('Pending', 'Succeeded')");
        });
    }
}
