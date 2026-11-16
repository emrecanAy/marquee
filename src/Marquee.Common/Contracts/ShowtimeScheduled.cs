using Marquee.Common.Messaging;

namespace Marquee.Common.Contracts;

/// <summary>Published by Catalog. Booking builds its sellable seat inventory from it.</summary>
public sealed record ShowtimeScheduled(
    Guid ShowtimeId,
    string MovieTitle,
    string HallName,
    DateTimeOffset StartsAt,
    IReadOnlyList<SeatOffer> Seats) : IIntegrationEvent;

public sealed record SeatOffer(string Label, string Row, int Number, string Category, decimal Price);
