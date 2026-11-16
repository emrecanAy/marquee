using Marquee.Common.Contracts;

namespace Marquee.Catalog.Domain;

internal sealed class Hall
{
    public const string StandardCategory = "standard";
    public const string PremiumCategory = "premium";
    private const decimal PremiumMultiplier = 1.4m;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; init; }

    public required int RowCount { get; init; }

    public required int SeatsPerRow { get; init; }

    /// <summary>The rows furthest from the screen are sold as premium.</summary>
    public required int PremiumRowCount { get; init; }

    /// <summary>Lays out every seat of the hall, row A nearest the screen, with its price for a showtime.</summary>
    public IReadOnlyList<SeatOffer> PriceSeats(decimal basePrice)
    {
        var seats = new List<SeatOffer>(RowCount * SeatsPerRow);
        for (var rowIndex = 0; rowIndex < RowCount; rowIndex++)
        {
            var row = ((char)('A' + rowIndex)).ToString();
            var premium = rowIndex >= RowCount - PremiumRowCount;
            var price = premium ? decimal.Round(basePrice * PremiumMultiplier, 2) : basePrice;

            for (var number = 1; number <= SeatsPerRow; number++)
            {
                seats.Add(new SeatOffer($"{row}{number}", row, number, premium ? PremiumCategory : StandardCategory, price));
            }
        }

        return seats;
    }
}
