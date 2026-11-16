using Marquee.Booking.Domain;
using Marquee.Booking.Persistence;
using Marquee.Common.Contracts;
using Marquee.Common.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Booking.Screenings;

/// <summary>Puts a newly scheduled showtime on sale by creating its seat inventory.</summary>
internal sealed class ShowtimeScheduledHandler(BookingDbContext db) : IIntegrationEventHandler<ShowtimeScheduled>
{
    public async Task HandleAsync(ShowtimeScheduled scheduled, CancellationToken ct)
    {
        // The event can be delivered more than once; the screening row tells us it was already handled.
        if (await db.Screenings.AnyAsync(screening => screening.Id == scheduled.ShowtimeId, ct))
        {
            return;
        }

        db.Screenings.Add(new Screening
        {
            Id = scheduled.ShowtimeId,
            MovieTitle = scheduled.MovieTitle,
            HallName = scheduled.HallName,
            StartsAt = scheduled.StartsAt,
        });
        db.Seats.AddRange(scheduled.Seats.Select(offer => new Seat
        {
            ScreeningId = scheduled.ShowtimeId,
            Label = offer.Label,
            Row = offer.Row,
            Number = offer.Number,
            Category = offer.Category,
            Price = offer.Price,
        }));

        await db.SaveChangesAsync(ct);
    }
}
