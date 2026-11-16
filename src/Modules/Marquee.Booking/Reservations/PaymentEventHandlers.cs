using Marquee.Booking.Admission;
using Marquee.Booking.Domain;
using Marquee.Booking.Persistence;
using Marquee.Common.Contracts;
using Marquee.Common.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Booking.Reservations;

/// <summary>Turns a paid reservation into tickets, or asks for the money back if the seats are gone.</summary>
internal sealed class PaymentSucceededHandler(BookingDbContext db, SeatRelease release, WaitingRoom waitingRoom)
    : IIntegrationEventHandler<PaymentSucceeded>
{
    public async Task HandleAsync(PaymentSucceeded payment, CancellationToken ct)
    {
        var reservation = await db.Reservations.SingleAsync(r => r.Id == payment.ReservationId, ct);

        switch (reservation.Status)
        {
            case ReservationStatus.Confirmed:
                // Redelivery of an event that was already applied.
                return;

            case ReservationStatus.Pending:
                var seats = await db.Seats
                    .Where(seat => seat.ScreeningId == reservation.ScreeningId && seat.ReservationId == reservation.Id)
                    .ToListAsync(ct);
                seats.ForEach(seat => seat.Sell());
                reservation.Confirm();
                break;

            default:
                // The hold ran out (or was cancelled) while the charge was in flight and the seats may
                // already belong to someone else. The customer must not pay for nothing.
                db.Publish(new ReservationRejected(reservation.Id, payment.PaymentId, "reservation_no_longer_held"));
                break;
        }

        // If the expiry sweep changed the reservation since it was read, xmin makes this throw.
        // The outbox then redelivers the event and the handler takes the refund branch.
        await db.SaveChangesAsync(ct);

        if (reservation.Status == ReservationStatus.Confirmed)
        {
            // Sold seats are protected by their database status; the locks and the shopping slot are no longer needed.
            await release.UnlockAsync(reservation);
            await waitingRoom.LeaveAsync(reservation.ScreeningId, reservation.SessionId);
        }
    }
}
