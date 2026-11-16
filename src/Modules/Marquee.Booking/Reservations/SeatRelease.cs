using Marquee.Booking.Domain;
using Marquee.Booking.Persistence;
using Marquee.Booking.Seats;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Booking.Reservations;

/// <summary>Returns the seats of a reservation to sale. Shared by cancelling, expiring and re-selecting.</summary>
internal sealed class SeatRelease(BookingDbContext db, SeatLocks locks)
{
    /// <summary>Marks the seats available in the current unit of work. The caller saves.</summary>
    public async Task FreeSeatsAsync(Reservation reservation, CancellationToken ct)
    {
        var seats = await db.Seats
            .Where(seat => seat.ScreeningId == reservation.ScreeningId && seat.ReservationId == reservation.Id)
            .ToListAsync(ct);
        seats.ForEach(seat => seat.Release());
    }

    /// <summary>
    /// Drops the Redis locks. Call it after the database commit: if the process dies in between,
    /// the locks simply run out on their own and the seats were already free where it counts.
    /// </summary>
    public Task UnlockAsync(Reservation reservation) =>
        locks.ReleaseAsync(reservation.ScreeningId, reservation.SeatLabels, reservation.Id);
}
