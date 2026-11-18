using Marquee.Catalog.Persistence;
using Marquee.Common.Results;
using Marquee.Common.Security;
using Marquee.Common.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Catalog.Features;

/// <summary>Planning the programme: staff with the scheduling permission put films in halls.</summary>
internal static class ProgrammeEndpoints
{
    public static readonly Permission Schedule = new("catalog.schedule", "Seans planlar", Roles.Programmer);

    /// <summary>Between two showtimes in one hall: the audience leaves and the hall is cleaned.</summary>
    public const int TurnaroundMinutes = 20;

    public sealed record HallResponse(Guid Id, string Name, int Seats);

    public sealed record PlannedShowtime(
        Guid Id, Guid MovieId, string MovieTitle, Guid HallId, string HallName, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
        decimal BasePrice);

    public sealed record ScheduleRequest(Guid MovieId, Guid HallId, DateTimeOffset StartsAt, decimal BasePrice);

    public static void Map(IEndpointRouteBuilder api)
    {
        var programme = api.MapGroup("/programme").RequirePermission(Schedule).WithTags("Programme");
        programme.MapGet("/halls", async (CatalogDbContext db, CancellationToken ct) =>
            await db.Halls.OrderBy(hall => hall.Name)
                .Select(hall => new HallResponse(hall.Id, hall.Name, hall.RowCount * hall.SeatsPerRow))
                .ToListAsync(ct));
        programme.MapGet("/showtimes", ListAsync);
        programme.MapPost("/showtimes", ScheduleAsync);
    }

    private static async Task<IReadOnlyList<PlannedShowtime>> ListAsync(CatalogDbContext db, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return await db.Showtimes
            .Where(showtime => showtime.StartsAt > now)
            .OrderBy(showtime => showtime.StartsAt)
            .Take(200)
            .Select(showtime => new PlannedShowtime(
                showtime.Id, showtime.MovieId, showtime.Movie.Title, showtime.HallId, showtime.Hall.Name, showtime.StartsAt,
                showtime.StartsAt.AddMinutes(showtime.Movie.DurationMinutes), showtime.BasePrice))
            .ToListAsync(ct);
    }

    /// <summary>
    /// A new showtime goes on sale through the same scheduler the demo programme uses. Two programmers
    /// planning the same hall at the same moment take turns on a lock for that hall, so the overlap
    /// check cannot pass for both.
    /// </summary>
    private static async Task<IResult> ScheduleAsync(
        ScheduleRequest request, CatalogDbContext db, ShowtimeScheduler scheduler, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (request.StartsAt < now.AddHours(1))
        {
            return Error.Validation("starts_too_soon", "A showtime starts at least an hour from now.").ToProblem();
        }

        if (request.BasePrice is <= 0 or > 10_000 || decimal.Round(request.BasePrice, 2) != request.BasePrice)
        {
            return Error.Validation("invalid_price", "The base price is a positive amount with at most two decimals.").ToProblem();
        }

        var movie = await db.Movies.SingleOrDefaultAsync(m => m.Id == request.MovieId, ct);
        var hall = await db.Halls.SingleOrDefaultAsync(h => h.Id == request.HallId, ct);
        if (movie is null || hall is null)
        {
            return Error.NotFound("movie_or_hall_not_found", "The film or the hall does not exist.").ToProblem();
        }

        var startsAt = request.StartsAt.ToUniversalTime();
        var busyUntil = startsAt.AddMinutes(movie.DurationMinutes + TurnaroundMinutes);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var hallKey = $"catalog:hall:{hall.Id}";
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({hallKey}, 0))", ct);

        var clash = await db.Showtimes
            .Where(other => other.HallId == hall.Id
                && other.StartsAt < busyUntil
                && other.StartsAt.AddMinutes(other.Movie.DurationMinutes + TurnaroundMinutes) > startsAt)
            .Select(other => new { other.Movie.Title, other.StartsAt })
            .FirstOrDefaultAsync(ct);
        if (clash is not null)
        {
            return Error.Conflict(
                    "hall_busy",
                    $"{hall.Name} is showing {clash.Title} at {clash.StartsAt:HH:mm} UTC; leave {TurnaroundMinutes} minutes between showings.")
                .ToProblem();
        }

        var showtime = scheduler.Schedule(movie, hall, startsAt, request.BasePrice);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Results.Created($"/api/showtimes/{showtime.Id}", new PlannedShowtime(
            showtime.Id, movie.Id, movie.Title, hall.Id, hall.Name, showtime.StartsAt,
            showtime.StartsAt.AddMinutes(movie.DurationMinutes), showtime.BasePrice));
    }
}
