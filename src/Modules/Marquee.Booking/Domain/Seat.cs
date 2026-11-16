using Marquee.Common.Results;

namespace Marquee.Booking.Domain;

internal enum SeatStatus
{
    Available,
    Held,
    Sold,
}

/// <summary>One seat of one screening. The row in PostgreSQL is the final word on who owns it.</summary>
internal sealed class Seat
{
    public required Guid ScreeningId { get; init; }

    public required string Label { get; init; }

    public required string Row { get; init; }

    public required int Number { get; init; }

    public required string Category { get; init; }

    public required decimal Price { get; init; }

    public SeatStatus Status { get; private set; } = SeatStatus.Available;

    public Guid? ReservationId { get; private set; }

    /// <summary>
    /// Mapped to PostgreSQL's <c>xmin</c> system column. Every UPDATE carries the value that was
    /// read, so of two transactions changing the same seat only the first one commits.
    /// </summary>
    public uint Version { get; private set; }

    public Result Hold(Guid reservationId)
    {
        if (Status != SeatStatus.Available)
        {
            return Error.Conflict("seat_taken", $"Seat {Label} is no longer available.");
        }

        Status = SeatStatus.Held;
        ReservationId = reservationId;
        return Result.Success;
    }

    public void Release()
    {
        if (Status == SeatStatus.Held)
        {
            Status = SeatStatus.Available;
            ReservationId = null;
        }
    }

    public void Sell()
    {
        if (Status != SeatStatus.Held)
        {
            throw new InvalidOperationException($"Seat {Label} must be held before it can be sold.");
        }

        Status = SeatStatus.Sold;
    }
}
