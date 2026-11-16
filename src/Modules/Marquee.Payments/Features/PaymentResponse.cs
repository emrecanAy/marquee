using Marquee.Payments.Domain;

namespace Marquee.Payments.Features;

internal sealed record PaymentResponse(Guid Id, Guid ReservationId, decimal Amount, string Status, string? FailureReason)
{
    public static PaymentResponse From(Payment payment) => new(
        payment.Id, payment.ReservationId, payment.Amount, payment.Status.ToString().ToLowerInvariant(),
        payment.FailureReason);
}
