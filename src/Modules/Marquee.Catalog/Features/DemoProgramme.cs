using Marquee.Catalog.Domain;
using Marquee.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Catalog.Features;

/// <summary>
/// Fills an empty database with a small programme so the application is usable right after
/// <c>docker compose up</c>, and keeps a few days of upcoming showtimes on sale after that.
/// </summary>
internal sealed class DemoProgramme(CatalogDbContext db, ShowtimeScheduler scheduler, TimeProvider time)
{
    private const int DaysOnSale = 4;

    // Showtimes are planned in cinema-local time (UTC+3) and stored in UTC.
    private static readonly TimeSpan CinemaUtcOffset = TimeSpan.FromHours(3);

    private static readonly TimeOnly[] DailySlots = [new(11, 0), new(14, 30), new(18, 0), new(21, 15)];

    public async Task EnsureAsync(CancellationToken ct)
    {
        if (!await db.Movies.AnyAsync(ct))
        {
            db.Movies.AddRange(Movies());
            db.Halls.AddRange(Halls());
            await db.SaveChangesAsync(ct);
        }

        var now = time.GetUtcNow();
        if (await db.Showtimes.AnyAsync(showtime => showtime.StartsAt > now, ct))
        {
            return;
        }

        var movies = await db.Movies.OrderBy(movie => movie.Title).ToListAsync(ct);
        var halls = await db.Halls.OrderBy(hall => hall.Name).ToListAsync(ct);
        var today = DateOnly.FromDateTime(now.ToOffset(CinemaUtcOffset).DateTime);

        for (var day = 0; day < DaysOnSale; day++)
        {
            // Starting one film later each day gives every movie a different mix of halls and hours.
            var rotation = day;
            foreach (var hall in halls)
            {
                foreach (var slot in DailySlots)
                {
                    var startsAt = new DateTimeOffset(today.AddDays(day).ToDateTime(slot), CinemaUtcOffset);
                    var movie = movies[rotation++ % movies.Count];
                    if (startsAt > now)
                    {
                        scheduler.Schedule(movie, hall, startsAt, basePrice: slot.Hour < 17 ? 180m : 240m);
                    }
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static Hall[] Halls() =>
    [
        new() { Name = "Hall 1 - Atlas", RowCount = 9, SeatsPerRow = 14, PremiumRowCount = 2 },
        new() { Name = "Hall 2 - Lumen", RowCount = 7, SeatsPerRow = 10, PremiumRowCount = 2 },
        new() { Name = "Hall 3 - Studio", RowCount = 5, SeatsPerRow = 8, PremiumRowCount = 1 },
    ];

    private static Movie[] Movies() =>
    [
        new()
        {
            Title = "The Last Ferry to Kadikoy",
            Tagline = "One crossing. Twenty minutes. Everything unsaid.",
            Synopsis = "A retired ferry captain takes the wheel for a final night crossing of the Bosphorus and finds "
                + "his estranged daughter among the passengers. As fog closes in, the twenty-minute trip stretches "
                + "into the conversation they have avoided for fifteen years.",
            Genre = "Drama", Director = "Selin Arkan", DurationMinutes = 112, AgeRating = "13+", PosterHue = 205,
        },
        new()
        {
            Title = "Orbit Decay",
            Tagline = "Gravity always collects.",
            Synopsis = "When a salvage crew boards a silent research station spiralling toward the atmosphere, they "
                + "have ninety minutes to recover its data core. The station's last log entry suggests the crew "
                + "never left, and that something on board would rather burn than be found.",
            Genre = "Sci-Fi", Director = "Mira Castellan", DurationMinutes = 127, AgeRating = "16+", PosterHue = 262,
        },
        new()
        {
            Title = "Bread, Salt, and a Wedding",
            Tagline = "Two families. One oven. No chance of peace.",
            Synopsis = "Rival bakers on the same Izmir street discover that their children are secretly engaged. "
                + "Forced to cater the wedding together, they wage a war of sourdough and sabotage that the whole "
                + "neighbourhood is delighted to join.",
            Genre = "Comedy", Director = "Deniz Oral", DurationMinutes = 98, AgeRating = "7+", PosterHue = 28,
        },
        new()
        {
            Title = "The Cartographer's Daughter",
            Tagline = "Some maps are drawn to be followed only once.",
            Synopsis = "Inheriting her father's unfinished atlas, a young surveyor notices a valley that appears on "
                + "no other chart. Her journey to find it leads across the Taurus mountains and into the reason "
                + "he never completed the map.",
            Genre = "Adventure", Director = "Tomas Ilves", DurationMinutes = 134, AgeRating = "10+", PosterHue = 152,
        },
        new()
        {
            Title = "Night Shift at the Grand Pera",
            Tagline = "Check-in is easy.",
            Synopsis = "A new night porter at a fading Beyoglu hotel is told one rule: never answer the phone from "
                + "room 407. On his third night it rings for an hour, and the guest register shows the room has "
                + "been occupied by the same name since 1961.",
            Genre = "Thriller", Director = "Kerem Aydin", DurationMinutes = 104, AgeRating = "18+", PosterHue = 348,
        },
        new()
        {
            Title = "Paper Kites Over Cappadocia",
            Tagline = "The sky is big enough for a small plan.",
            Synopsis = "Three cousins spend a summer with their grandmother and decide to win the valley's kite "
                + "festival with a design nobody believes can fly. An animated adventure about stubbornness, wind "
                + "and glue.",
            Genre = "Animation", Director = "Hana Okafor", DurationMinutes = 91, AgeRating = "All", PosterHue = 48,
        },
    ];
}
