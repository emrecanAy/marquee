namespace Marquee.Booking.Admission;

internal enum QueueState
{
    /// <summary>Neither waiting nor admitted.</summary>
    None,
    Waiting,
    Admitted,
}

/// <param name="Position">1-based place in line while waiting, otherwise 0.</param>
internal sealed record QueueTicket(QueueState State, int Position, DateTimeOffset? AdmittedUntil);
