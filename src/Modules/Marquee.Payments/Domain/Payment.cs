namespace Marquee.Payments.Domain;

internal enum PaymentStatus
{
    /// <summary>Recorded, charge not attempted or not finished yet.</summary>
    Pending,
    Succeeded,
    Failed,
    Refunded,
}

internal sealed class Payment
{
    private Payment()
    {
    }

    public Guid Id { get; private set; }

    public Guid ReservationId { get; private set; }

    public Guid SessionId { get; private set; }

    public string IdempotencyKey { get; private set; } = null!;

    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SettledAt { get; private set; }

    public uint Version { get; private set; }

    public static Payment Start(Guid reservationId, Guid sessionId, string idempotencyKey, decimal amount, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            ReservationId = reservationId,
            SessionId = sessionId,
            IdempotencyKey = idempotencyKey,
            Amount = amount,
            Status = PaymentStatus.Pending,
            CreatedAt = now,
        };

    public void Succeed(DateTimeOffset now) => Settle(PaymentStatus.Succeeded, null, now);

    public void Fail(string reason, DateTimeOffset now) => Settle(PaymentStatus.Failed, reason, now);

    public void Refund(string reason, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Succeeded)
        {
            throw new InvalidOperationException($"Only a succeeded payment can be refunded; this one is {Status}.");
        }

        Status = PaymentStatus.Refunded;
        FailureReason = reason;
        SettledAt = now;
    }

    private void Settle(PaymentStatus outcome, string? reason, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException($"Payment {Id} was already settled as {Status}.");
        }

        Status = outcome;
        FailureReason = reason;
        SettledAt = now;
    }
}
