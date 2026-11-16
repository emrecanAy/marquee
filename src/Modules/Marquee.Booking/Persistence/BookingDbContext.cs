using Marquee.Booking.Domain;
using Marquee.Common.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Booking.Persistence;

internal sealed class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public const string Schema = "booking";

    public DbSet<Screening> Screenings => Set<Screening>();

    public DbSet<Seat> Seats => Set<Seat>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema).AddOutbox();

        modelBuilder.Entity<Screening>(screening =>
        {
            screening.Property(s => s.Id).ValueGeneratedNever();
            screening.Property(s => s.MovieTitle).HasMaxLength(200);
            screening.Property(s => s.HallName).HasMaxLength(100);
        });

        modelBuilder.Entity<Seat>(seat =>
        {
            seat.HasKey(s => new { s.ScreeningId, s.Label });
            seat.Property(s => s.Label).HasMaxLength(8);
            seat.Property(s => s.Row).HasMaxLength(4);
            seat.Property(s => s.Category).HasMaxLength(20);
            seat.Property(s => s.Price).HasPrecision(10, 2);
            seat.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            seat.Property(s => s.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
            seat.HasOne<Screening>().WithMany().HasForeignKey(s => s.ScreeningId);
            seat.HasIndex(s => s.ReservationId);
        });

        modelBuilder.Entity<Reservation>(reservation =>
        {
            reservation.Property(r => r.Id).ValueGeneratedNever();
            reservation.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            reservation.Property(r => r.Amount).HasPrecision(10, 2);
            reservation.Property(r => r.TicketCode).HasMaxLength(20);
            reservation.Property(r => r.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
            reservation.HasOne<Screening>().WithMany().HasForeignKey(r => r.ScreeningId);
            reservation.HasIndex(r => r.SessionId);
            // Only open holds can expire, so the sweeper's index covers just those rows.
            reservation.HasIndex(r => r.ExpiresAt).HasFilter("status = 'Pending'");
        });
    }
}
