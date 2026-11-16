namespace Marquee.Booking.Domain;

/// <summary>
/// Booking's own copy of a showtime, built from the <c>ShowtimeScheduled</c> event. Keeping it
/// here means selling tickets never needs a call into the Catalog module.
/// </summary>
internal sealed class Screening
{
    public required Guid Id { get; init; }

    public required string MovieTitle { get; init; }

    public required string HallName { get; init; }

    public required DateTimeOffset StartsAt { get; init; }
}
