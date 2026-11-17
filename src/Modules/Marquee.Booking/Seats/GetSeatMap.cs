using System.Security.Claims;
using Marquee.Booking.Domain;
using Marquee.Booking.Persistence;
using Marquee.Booking.Reservations;
using Marquee.Common.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Booking.Seats;

internal static class GetSeatMap
{
    /// <param name="Status"><c>available</c>, <c>held</c>, <c>sold</c>, or <c>mine</c> for seats the caller is holding.</param>
    public sealed record SeatResponse(string Label, string Row, int Number, string Category, decimal Price, string Status);

    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/showtimes/{showtimeId:guid}/seats", HandleAsync).RequireAuthorization().WithTags("Booking");

    private static async Task<IReadOnlyList<SeatResponse>> HandleAsync(
        Guid showtimeId, ClaimsPrincipal user, BookingDbContext db, CancellationToken ct)
    {
        var myHolds = await db.Reservations
            .Where(Ownership.OwnedBy(user.GetTicketOwner()))
            .Where(reservation => reservation.ScreeningId == showtimeId && reservation.Status == ReservationStatus.Pending)
            .Select(reservation => reservation.Id)
            .ToListAsync(ct);

        var seats = await db.Seats.AsNoTracking()
            .Where(seat => seat.ScreeningId == showtimeId)
            .OrderBy(seat => seat.Row).ThenBy(seat => seat.Number)
            .ToListAsync(ct);

        return seats.ConvertAll(seat => new SeatResponse(
            seat.Label, seat.Row, seat.Number, seat.Category, seat.Price,
            seat.ReservationId is { } holder && myHolds.Contains(holder) && seat.Status == SeatStatus.Held
                ? "mine"
                : seat.Status.ToString().ToLowerInvariant()));
    }
}
