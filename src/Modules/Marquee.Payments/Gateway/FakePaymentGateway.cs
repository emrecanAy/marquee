using System.Collections.Concurrent;

namespace Marquee.Payments.Gateway;

/// <summary>
/// Stands in for a real provider. It behaves like one where it matters: it is slow, it can
/// decline, and it treats the payment id as an idempotency key, so charging twice for the same
/// payment returns the first answer instead of taking the money again.
/// </summary>
internal sealed class FakePaymentGateway : IPaymentGateway
{
    public const string DeclinedToken = "tok_declined";
    public const string SlowToken = "tok_slow";

    private readonly ConcurrentDictionary<Guid, ChargeResult> _charges = new();

    public async Task<ChargeResult> ChargeAsync(Guid paymentId, decimal amount, string paymentToken, CancellationToken ct)
    {
        if (_charges.TryGetValue(paymentId, out var earlier))
        {
            return earlier;
        }

        await Task.Delay(paymentToken == SlowToken ? TimeSpan.FromSeconds(3) : TimeSpan.FromMilliseconds(600), ct);

        var result = paymentToken == DeclinedToken ? ChargeResult.Declined("card_declined") : ChargeResult.Ok;
        return _charges.GetOrAdd(paymentId, result);
    }

    public Task RefundAsync(Guid paymentId, CancellationToken ct) => Task.CompletedTask;
}
