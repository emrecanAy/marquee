using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marquee.Booking.Admission;

internal sealed class WaitingRoomDispatcher(
    WaitingRoom waitingRoom,
    IOptions<WaitingRoomOptions> options,
    ILogger<WaitingRoomDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.DispatchInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var admitted = await waitingRoom.AdmitWaitingAsync();
                if (admitted > 0)
                {
                    logger.LogInformation("Admitted {Count} waiting customer(s)", admitted);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Waiting room dispatch failed");
            }
        }
    }
}
