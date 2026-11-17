using Marquee.Common.Results;
using Marquee.Common.Web;

namespace Marquee.Common.Contracts;

/// <summary>What Payments is allowed to ask Booking. Implemented by the Booking module.</summary>
public interface IReservationCheckout
{
    /// <summary>
    /// Checks that the reservation belongs to this visitor and is still payable, and keeps its seats
    /// held long enough for the payment to complete.
    /// </summary>
    Task<Result<CheckoutQuote>> BeginPaymentAsync(Guid reservationId, TicketOwner owner, CancellationToken ct);
}

public sealed record CheckoutQuote(Guid ReservationId, decimal Amount);
