using Marquee.Common.Results;

namespace Marquee.Common.Contracts;

/// <summary>What Payments is allowed to ask Booking. Implemented by the Booking module.</summary>
public interface IReservationCheckout
{
    /// <summary>
    /// Checks that the reservation is still payable by this session and keeps its seats held
    /// long enough for the payment to complete.
    /// </summary>
    Task<Result<CheckoutQuote>> BeginPaymentAsync(Guid reservationId, Guid sessionId, CancellationToken ct);
}

public sealed record CheckoutQuote(Guid ReservationId, decimal Amount);
