namespace Marquee.Booking.Admission;

public sealed class WaitingRoomOptions
{
    /// <summary>How many customers may pick seats for one screening at the same time.</summary>
    public int Capacity { get; set; } = 50;

    /// <summary>How long an admitted customer may stay in seat selection before the place is given away.</summary>
    public TimeSpan AdmissionWindow { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>A waiting customer who has not polled for this long is assumed to have closed the tab.</summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan DispatchInterval { get; set; } = TimeSpan.FromSeconds(2);
}
