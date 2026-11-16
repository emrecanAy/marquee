using Marquee.Common.Contracts;
using Marquee.Common.Messaging;
using Marquee.Payments.Domain;
using Marquee.Payments.Gateway;
using Marquee.Payments.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Payments.Features;

/// <summary>Compensation: Booking could not honour a paid reservation, so the money goes back.</summary>
internal sealed class ReservationRejectedHandler(PaymentsDbContext db, IPaymentGateway gateway, TimeProvider time)
    : IIntegrationEventHandler<ReservationRejected>
{
    public async Task HandleAsync(ReservationRejected rejected, CancellationToken ct)
    {
        var payment = await db.Payments.SingleAsync(p => p.Id == rejected.PaymentId, ct);
        if (payment.Status != PaymentStatus.Succeeded)
        {
            return;
        }

        await gateway.RefundAsync(payment.Id, ct);
        payment.Refund(rejected.Reason, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }
}
