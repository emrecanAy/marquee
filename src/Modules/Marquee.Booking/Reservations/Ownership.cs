using System.Linq.Expressions;
using Marquee.Booking.Domain;
using Marquee.Common.Web;

namespace Marquee.Booking.Reservations;

internal static class Ownership
{
    /// <summary>
    /// The visitor's own reservations: those made in this browser session, and those of the signed-in
    /// account wherever they were made.
    /// </summary>
    public static Expression<Func<Reservation, bool>> OwnedBy(TicketOwner owner) =>
        owner.AccountId is { } accountId
            ? reservation => reservation.SessionId == owner.SessionId || reservation.AccountId == accountId
            : reservation => reservation.SessionId == owner.SessionId;
}
