using System.Security.Claims;
using Marquee.Booking.Domain;
using Marquee.Booking.Persistence;
using Marquee.Common.Results;
using Marquee.Common.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Booking.Reservations;

internal static class ReservationEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var reservations = api.MapGroup("/reservations").RequireAuthorization().WithTags("Booking");
        reservations.MapGet("", ListMineAsync);
        reservations.MapGet("/{reservationId:guid}", GetAsync);
        reservations.MapDelete("/{reservationId:guid}", CancelAsync);
    }

    private static async Task<IReadOnlyList<ReservationResponse>> ListMineAsync(
        ClaimsPrincipal user, BookingDbContext db, CancellationToken ct)
    {
        var rows = await db.Reservations.AsNoTracking()
            .Where(Ownership.OwnedBy(user.GetTicketOwner()))
            .Where(reservation => reservation.Status == ReservationStatus.Confirmed)
            .Join(db.Screenings, reservation => reservation.ScreeningId, screening => screening.Id,
                (reservation, screening) => new { reservation, screening })
            .OrderByDescending(row => row.screening.StartsAt)
            .ToListAsync(ct);

        return rows.ConvertAll(row => ReservationResponse.From(row.reservation, row.screening));
    }

    private static async Task<IResult> GetAsync(
        Guid reservationId, ClaimsPrincipal user, BookingDbContext db, CancellationToken ct)
    {
        var reservation = await FindOwnAsync(db.Reservations.AsNoTracking(), reservationId, user, ct);
        if (reservation is null)
        {
            return NotFound().ToProblem();
        }

        var screening = await db.Screenings.AsNoTracking().SingleAsync(s => s.Id == reservation.ScreeningId, ct);
        return Results.Ok(ReservationResponse.From(reservation, screening));
    }

    private static async Task<IResult> CancelAsync(
        Guid reservationId, ClaimsPrincipal user, BookingDbContext db, SeatRelease release, CancellationToken ct)
    {
        var reservation = await FindOwnAsync(db.Reservations, reservationId, user, ct);
        if (reservation is null)
        {
            return NotFound().ToProblem();
        }

        var cancelled = reservation.Cancel();
        if (!cancelled.IsSuccess)
        {
            return cancelled.Error.ToProblem();
        }

        await release.FreeSeatsAsync(reservation, ct);
        await db.SaveChangesAsync(ct);
        await release.UnlockAsync(reservation);
        return Results.NoContent();
    }

    // Someone else's reservation is reported as missing rather than forbidden, so ids cannot be probed.
    private static Task<Reservation?> FindOwnAsync(
        IQueryable<Reservation> reservations, Guid reservationId, ClaimsPrincipal user, CancellationToken ct)
    {
        return reservations.Where(Ownership.OwnedBy(user.GetTicketOwner())).SingleOrDefaultAsync(
            reservation => reservation.Id == reservationId, ct);
    }

    private static Error NotFound() => Error.NotFound("reservation_not_found", "This reservation does not exist.");
}
