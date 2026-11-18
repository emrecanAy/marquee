using System.Security.Cryptography;
using Marquee.Common.Results;
using Marquee.Common.Web;

namespace Marquee.Booking.Domain;

internal enum ReservationStatus
{
    /// <summary>Seats are held and waiting for payment.</summary>
    Pending,
    Confirmed,
    Cancelled,
    Expired,
}

internal sealed class Reservation
{
    private Reservation()
    {
    }

    public Guid Id { get; private set; }

    public Guid ScreeningId { get; private set; }

    public Guid SessionId { get; private set; }

    /// <summary>The account the seats belong to, once the visitor has signed in; empty for a guest.</summary>
    public Guid? AccountId { get; private set; }

    public ReservationStatus Status { get; private set; }

    public List<string> SeatLabels { get; private set; } = [];

    public decimal Amount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public string? TicketCode { get; private set; }

    /// <summary>PostgreSQL <c>xmin</c>; decides the race between "payment arrived" and "hold expired".</summary>
    public uint Version { get; private set; }

    public static Reservation Hold(
        Guid id, Guid screeningId, TicketOwner owner, IReadOnlyCollection<Seat> seats, DateTimeOffset now, TimeSpan holdFor) =>
        new()
        {
            Id = id,
            ScreeningId = screeningId,
            SessionId = owner.SessionId,
            AccountId = owner.AccountId,
            Status = ReservationStatus.Pending,
            SeatLabels = seats.Select(seat => seat.Label).Order(StringComparer.Ordinal).ToList(),
            Amount = seats.Sum(seat => seat.Price),
            CreatedAt = now,
            ExpiresAt = now + holdFor,
        };

    /// <summary>
    /// Called when the customer submits payment. The hold is stretched so that a charge started
    /// in the last seconds can still land, but never beyond <paramref name="latestExpiry"/> after creation.
    /// </summary>
    public Result BeginPayment(DateTimeOffset now, TimeSpan grace, TimeSpan latestExpiry)
    {
        if (Status != ReservationStatus.Pending)
        {
            return NotPending();
        }

        if (now >= ExpiresAt)
        {
            return Error.Conflict("reservation_expired", "The hold on these seats has run out.");
        }

        var stretched = now + grace;
        var limit = CreatedAt + latestExpiry;
        if (stretched > limit)
        {
            stretched = limit;
        }

        if (stretched > ExpiresAt)
        {
            ExpiresAt = stretched;
        }

        return Result.Success;
    }

    public Result Confirm()
    {
        if (Status != ReservationStatus.Pending)
        {
            return NotPending();
        }

        Status = ReservationStatus.Confirmed;
        TicketCode = $"MQ-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}";
        return Result.Success;
    }

    public Result Cancel() => Close(ReservationStatus.Cancelled);

    public Result Expire() => Close(ReservationStatus.Expired);

    private Result Close(ReservationStatus closedAs)
    {
        if (Status != ReservationStatus.Pending)
        {
            return NotPending();
        }

        Status = closedAs;
        return Result.Success;
    }

    private Error NotPending() => Error.Conflict(
        "reservation_not_pending", $"This reservation is already {Status.ToString().ToLowerInvariant()}.");
}
