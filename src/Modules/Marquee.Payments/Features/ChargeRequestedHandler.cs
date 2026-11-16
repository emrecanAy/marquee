using Marquee.Common.Contracts;
using Marquee.Common.Messaging;
using Marquee.Payments.Domain;
using Marquee.Payments.Gateway;
using Marquee.Payments.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Payments.Features;

/// <summary>
/// Talks to the payment provider, outside the request that recorded the payment. The outcome
/// and the event announcing it are again committed together.
/// </summary>
internal sealed class ChargeRequestedHandler(PaymentsDbContext db, IPaymentGateway gateway, TimeProvider time)
    : IIntegrationEventHandler<PayReservation.ChargeRequested>
{
    public async Task HandleAsync(PayReservation.ChargeRequested request, CancellationToken ct)
    {
        var payment = await db.Payments.SingleAsync(p => p.Id == request.PaymentId, ct);
        if (payment.Status != PaymentStatus.Pending)
        {
            return;
        }

        // If the process dies after the provider answered but before the commit below, the message is
        // delivered again. The provider recognises the payment id and repeats its answer, so the
        // customer is charged once.
        var charge = await gateway.ChargeAsync(payment.Id, payment.Amount, request.PaymentToken, ct);

        var now = time.GetUtcNow();
        if (charge.Approved)
        {
            payment.Succeed(now);
            db.Publish(new PaymentSucceeded(payment.Id, payment.ReservationId));
        }
        else
        {
            payment.Fail(charge.DeclineReason ?? "declined", now);
            db.Publish(new PaymentFailed(payment.Id, payment.ReservationId, payment.FailureReason!));
        }

        await db.SaveChangesAsync(ct);
    }
}
