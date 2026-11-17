namespace Marquee.Common.Contracts;

/// <summary>What Identity may ask Booking when a guest signs in. Implemented by the Booking module.</summary>
public interface IGuestReservations
{
    /// <summary>Gives the seats and tickets this browser got as a guest to the account that just signed in.</summary>
    Task AttachToAccountAsync(Guid sessionId, Guid accountId, CancellationToken ct);
}
