namespace Marquee.Booking;

public sealed class BookingOptions
{
    /// <summary>How long selected seats stay reserved for a customer who has not paid yet.</summary>
    public TimeSpan HoldDuration { get; set; } = TimeSpan.FromMinutes(8);

    /// <summary>Extra time granted once payment has been submitted, so a slow charge does not lose its seats.</summary>
    public TimeSpan PaymentGrace { get; set; } = TimeSpan.FromMinutes(2);

    public int MaxSeatsPerReservation { get; set; } = 6;

    public TimeSpan ExpirySweepInterval { get; set; } = TimeSpan.FromSeconds(5);
}
