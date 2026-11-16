namespace Marquee.Catalog.Domain;

internal sealed class Movie
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Title { get; init; }

    public required string Tagline { get; init; }

    public required string Synopsis { get; init; }

    public required string Genre { get; init; }

    public required string Director { get; init; }

    public required int DurationMinutes { get; init; }

    public required string AgeRating { get; init; }

    /// <summary>Hue (0-359) the web client builds the poster artwork from. There are no image assets.</summary>
    public required int PosterHue { get; init; }
}
