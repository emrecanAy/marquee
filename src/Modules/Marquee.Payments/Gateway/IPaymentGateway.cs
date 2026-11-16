namespace Marquee.Payments.Gateway;

/// <summary>The payment provider. Both calls must be safe to repeat with the same payment id.</summary>
internal interface IPaymentGateway
{
    Task<ChargeResult> ChargeAsync(Guid paymentId, decimal amount, string paymentToken, CancellationToken ct);

    Task RefundAsync(Guid paymentId, CancellationToken ct);
}

internal sealed record ChargeResult(bool Approved, string? DeclineReason)
{
    public static readonly ChargeResult Ok = new(true, null);

    public static ChargeResult Declined(string reason) => new(false, reason);
}
