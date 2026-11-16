namespace Marquee.Catalog.Domain;

internal sealed class Showtime
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid MovieId { get; init; }

    public required Guid HallId { get; init; }

    public required DateTimeOffset StartsAt { get; init; }

    public required decimal BasePrice { get; init; }

    public Movie Movie { get; init; } = null!;

    public Hall Hall { get; init; } = null!;
}
