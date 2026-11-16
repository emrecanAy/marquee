using Marquee.Booking.Domain;
using Marquee.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marquee.Booking.Reservations;

/// <summary>Gives the seats of unpaid reservations back once their hold has run out.</summary>
internal sealed class ReservationExpiry(BookingDbContext db, SeatRelease release)
{
    private const int BatchSize = 50;

    /// <returns>How many reservations were expired; a full batch means there may be more.</returns>
    public async Task<int> ExpireOverdueAsync(DateTimeOffset now, CancellationToken ct)
    {
        // SKIP LOCKED: several application instances can sweep at once without waiting on each
        // other, and a reservation that is being confirmed right now is left alone.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var overdue = await db.Reservations
            .FromSql($"""
                SELECT *, xmin FROM booking.reservations
                WHERE status = 'Pending' AND expires_at <= {now}
                ORDER BY expires_at
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        foreach (var reservation in overdue)
        {
            reservation.Expire();
            await release.FreeSeatsAsync(reservation, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        foreach (var reservation in overdue)
        {
            await release.UnlockAsync(reservation);
        }

        return overdue.Count;
    }
}

internal sealed class ReservationExpiryWorker(
    IServiceScopeFactory scopes,
    IOptions<BookingOptions> options,
    TimeProvider time,
    ILogger<ReservationExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.ExpirySweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var expired = await SweepAsync(stoppingToken);
                if (expired > 0)
                {
                    logger.LogInformation("Expired {Count} unpaid reservation(s)", expired);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reservation expiry sweep failed");
            }
        }
    }

    private async Task<int> SweepAsync(CancellationToken ct)
    {
        var total = 0;
        int expired;
        do
        {
            // A fresh scope per batch keeps each DbContext small and each transaction short.
            await using var scope = scopes.CreateAsyncScope();
            expired = await scope.ServiceProvider.GetRequiredService<ReservationExpiry>()
                .ExpireOverdueAsync(time.GetUtcNow(), ct);
            total += expired;
        }
        while (expired > 0);

        return total;
    }
}
