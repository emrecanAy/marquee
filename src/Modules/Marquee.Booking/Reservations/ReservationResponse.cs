using Marquee.Booking.Domain;

namespace Marquee.Booking.Reservations;

internal sealed record ReservationResponse(
    Guid Id,
    Guid ShowtimeId,
    string MovieTitle,
    string HallName,
    DateTimeOffset StartsAt,
    IReadOnlyList<string> Seats,
    decimal Amount,
    string Status,
    DateTimeOffset ExpiresAt,
    string? TicketCode)
{
    public static ReservationResponse From(Reservation reservation, Screening screening) => new(
        reservation.Id,
        reservation.ScreeningId,
        screening.MovieTitle,
        screening.HallName,
        screening.StartsAt,
        reservation.SeatLabels,
        reservation.Amount,
        reservation.Status.ToString().ToLowerInvariant(),
        reservation.ExpiresAt,
        reservation.TicketCode);
}
