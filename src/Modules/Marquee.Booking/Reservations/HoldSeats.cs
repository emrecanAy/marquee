using System.Security.Claims;
using Marquee.Booking.Admission;
using Marquee.Booking.Domain;
using Marquee.Booking.Persistence;
using Marquee.Booking.Seats;
using Marquee.Common.Results;
using Marquee.Common.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marquee.Booking.Reservations;

internal static class HoldSeats
{
    public sealed record Request(IReadOnlyList<string>? Seats);

    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost("/showtimes/{showtimeId:guid}/reservations", async (
                Guid showtimeId, Request request, ClaimsPrincipal user, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(showtimeId, user.GetTicketOwner(), request.Seats ?? [], ct);
                return result.Match(
                    reservation => Results.Created($"/api/reservations/{reservation.Id}", reservation),
                    error => error.ToProblem());
            })
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.SeatHolds)
            .WithTags("Booking");

    internal sealed class Handler(
        BookingDbContext db,
        SeatLocks locks,
        SeatRelease release,
        WaitingRoom waitingRoom,
        IOptions<BookingOptions> options,
        TimeProvider time)
    {
        private readonly BookingOptions _options = options.Value;

        public async Task<Result<ReservationResponse>> HandleAsync(
            Guid screeningId, TicketOwner owner, IReadOnlyList<string> requestedSeats, CancellationToken ct)
        {
            var labels = requestedSeats.Distinct(StringComparer.Ordinal).ToList();
            if (labels.Count == 0 || labels.Count > _options.MaxSeatsPerReservation)
            {
                return Error.Validation("invalid_seat_count",
                    $"Choose between 1 and {_options.MaxSeatsPerReservation} seats.");
            }

            var now = time.GetUtcNow();
            var screening = await db.Screenings.FindAsync([screeningId], ct);
            if (screening is null)
            {
                return Error.NotFound("showtime_not_found", "This showtime does not exist.");
            }

            if (screening.StartsAt <= now)
            {
                return Error.Conflict("sales_closed", "Ticket sales for this showtime have closed.");
            }

            if (!await waitingRoom.IsAdmittedAsync(screeningId, owner.SessionId))
            {
                return Error.Forbidden("not_admitted", "Join the waiting room before choosing seats.");
            }

            // A customer who goes back and picks again swaps the old selection for the new one.
            await ReleasePreviousHoldsAsync(screeningId, owner.SessionId, ct);

            // 1. Redis: one request wins each seat, everyone else stops here without touching PostgreSQL.
            var reservationId = Guid.CreateVersion7();
            var taken = await locks.TryAcquireAsync(screeningId, labels, reservationId, _options.HoldDuration);
            if (taken is not null)
            {
                return SeatTaken(taken);
            }

            // 2. PostgreSQL: record the hold. The status check catches seats that are held or sold
            //    without a Redis key; the xmin version catches a transaction that changed a seat
            //    after this one read it.
            var result = await RecordHoldAsync(reservationId, screening, owner, labels, now, ct);
            if (!result.IsSuccess)
            {
                await locks.ReleaseAsync(screeningId, labels, reservationId);
            }

            return result;
        }

        private async Task ReleasePreviousHoldsAsync(Guid screeningId, Guid sessionId, CancellationToken ct)
        {
            var previousHolds = await db.Reservations
                .Where(reservation => reservation.ScreeningId == screeningId
                    && reservation.SessionId == sessionId
                    && reservation.Status == ReservationStatus.Pending)
                .ToListAsync(ct);
            if (previousHolds.Count == 0)
            {
                return;
            }

            foreach (var previous in previousHolds)
            {
                previous.Cancel();
                await release.FreeSeatsAsync(previous, ct);
            }

            await db.SaveChangesAsync(ct);

            foreach (var previous in previousHolds)
            {
                await release.UnlockAsync(previous);
            }
        }

        private async Task<Result<ReservationResponse>> RecordHoldAsync(
            Guid reservationId, Screening screening, TicketOwner owner, List<string> labels, DateTimeOffset now,
            CancellationToken ct)
        {
            var seats = await db.Seats
                .Where(seat => seat.ScreeningId == screening.Id && labels.Contains(seat.Label))
                .ToListAsync(ct);
            if (seats.Count != labels.Count)
            {
                return Error.NotFound("seat_not_found", "One of the chosen seats does not exist in this hall.");
            }

            foreach (var seat in seats)
            {
                var held = seat.Hold(reservationId);
                if (!held.IsSuccess)
                {
                    return held.Error;
                }
            }

            var reservation = Reservation.Hold(reservationId, screening.Id, owner, seats, now, _options.HoldDuration);
            db.Reservations.Add(reservation);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return SeatTaken(labels[0]);
            }

            return ReservationResponse.From(reservation, screening);
        }

        private static Error SeatTaken(string label) =>
            Error.Conflict("seat_taken", $"Seat {label} was just taken by someone else.");
    }
}
