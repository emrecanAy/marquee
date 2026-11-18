using Marquee.Booking.Persistence;
using Marquee.Booking.Seats;
using Marquee.Common.Contracts;
using Marquee.Common.Results;
using Marquee.Common.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marquee.Booking.Reservations;

internal sealed class ReservationCheckout(
    BookingDbContext db, SeatLocks locks, IOptions<BookingOptions> options, TimeProvider time) : IReservationCheckout
{
    public async Task<Result<CheckoutQuote>> BeginPaymentAsync(Guid reservationId, TicketOwner owner, CancellationToken ct)
    {
        var reservation = await db.Reservations.Where(Ownership.OwnedBy(owner)).SingleOrDefaultAsync(r => r.Id == reservationId, ct);
        if (reservation is null)
        {
            return Error.NotFound("reservation_not_found", "This reservation does not exist.");
        }

        var now = time.GetUtcNow();
        var began = reservation.BeginPayment(
            now, options.Value.PaymentGrace, options.Value.HoldDuration + options.Value.PaymentGrace);
        if (!began.IsSuccess)
        {
            return began.Error;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The expiry sweep (or a cancel) got to the reservation between our read and our write.
            return Error.Conflict("reservation_expired", "The hold on these seats has run out.");
        }

        await locks.ExtendAsync(reservation.ScreeningId, reservation.SeatLabels, reservation.Id, reservation.ExpiresAt - now);
        return new CheckoutQuote(reservation.Id, reservation.Amount);
    }
}
