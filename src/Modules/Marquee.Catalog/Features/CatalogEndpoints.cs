using Marquee.Catalog.Persistence;
using Marquee.Common.Results;
using Marquee.Common.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Catalog.Features;

internal static class CatalogEndpoints
{
    public sealed record MovieSummary(
        Guid Id, string Title, string Tagline, string Genre, int DurationMinutes, string AgeRating, int PosterHue,
        DateTimeOffset? NextShowtime);

    public sealed record MovieDetails(
        Guid Id, string Title, string Tagline, string Synopsis, string Genre, string Director, int DurationMinutes,
        string AgeRating, int PosterHue, IReadOnlyList<ShowtimeSlot> Showtimes);

    public sealed record ShowtimeSlot(Guid Id, DateTimeOffset StartsAt, string HallName, decimal PriceFrom);

    public sealed record ShowtimeDetails(
        Guid Id, DateTimeOffset StartsAt, Guid MovieId, string MovieTitle, int PosterHue, int DurationMinutes,
        string HallName, int RowCount, int SeatsPerRow);

    public static void Map(IEndpointRouteBuilder api)
    {
        // The programme is public: anyone may browse it without a session.
        var catalog = api.MapGroup("").AllowAnonymous().WithTags("Catalog");
        catalog.MapGet("/movies", ListMovies);
        catalog.MapGet("/movies/{movieId:guid}", GetMovie);
        catalog.MapGet("/showtimes/{showtimeId:guid}", GetShowtime);
    }

    private static async Task<IReadOnlyList<MovieSummary>> ListMovies(
        CatalogDbContext db, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return await db.Movies
            .OrderBy(movie => movie.Title)
            .Select(movie => new MovieSummary(
                movie.Id, movie.Title, movie.Tagline, movie.Genre, movie.DurationMinutes, movie.AgeRating,
                movie.PosterHue,
                db.Showtimes
                    .Where(showtime => showtime.MovieId == movie.Id && showtime.StartsAt > now)
                    .Min(showtime => (DateTimeOffset?)showtime.StartsAt)))
            .ToListAsync(ct);
    }

    private static async Task<IResult> GetMovie(
        Guid movieId, CatalogDbContext db, TimeProvider time, CancellationToken ct)
    {
        var movie = await db.Movies.AsNoTracking().SingleOrDefaultAsync(m => m.Id == movieId, ct);
        if (movie is null)
        {
            return Error.NotFound("movie_not_found", "This movie is not in the programme.").ToProblem();
        }

        var now = time.GetUtcNow();
        var showtimes = await db.Showtimes
            .Where(showtime => showtime.MovieId == movieId && showtime.StartsAt > now)
            .OrderBy(showtime => showtime.StartsAt)
            .Select(showtime => new ShowtimeSlot(showtime.Id, showtime.StartsAt, showtime.Hall.Name, showtime.BasePrice))
            .ToListAsync(ct);

        return Results.Ok(new MovieDetails(
            movie.Id, movie.Title, movie.Tagline, movie.Synopsis, movie.Genre, movie.Director,
            movie.DurationMinutes, movie.AgeRating, movie.PosterHue, showtimes));
    }

    private static async Task<IResult> GetShowtime(Guid showtimeId, CatalogDbContext db, CancellationToken ct)
    {
        var showtime = await db.Showtimes
            .Where(s => s.Id == showtimeId)
            .Select(s => new ShowtimeDetails(
                s.Id, s.StartsAt, s.MovieId, s.Movie.Title, s.Movie.PosterHue, s.Movie.DurationMinutes,
                s.Hall.Name, s.Hall.RowCount, s.Hall.SeatsPerRow))
            .SingleOrDefaultAsync(ct);

        return showtime is null
            ? Error.NotFound("showtime_not_found", "This showtime does not exist.").ToProblem()
            : Results.Ok(showtime);
    }
}
