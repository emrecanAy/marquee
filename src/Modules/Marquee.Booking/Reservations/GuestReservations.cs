using Marquee.Booking.Persistence;
using Marquee.Common.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Booking.Reservations;

/// <summary>On sign-in, what the browser picked or bought as a guest becomes the account's.</summary>
internal sealed class GuestReservations(BookingDbContext db) : IGuestReservations
{
    public Task AttachToAccountAsync(Guid sessionId, Guid accountId, CancellationToken ct) =>
        db.Reservations
            .Where(reservation => reservation.SessionId == sessionId && reservation.AccountId == null)
            .ExecuteUpdateAsync(set => set.SetProperty(reservation => reservation.AccountId, accountId), ct);
}
