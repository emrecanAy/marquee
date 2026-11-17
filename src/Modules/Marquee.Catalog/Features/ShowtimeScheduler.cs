using Marquee.Catalog.Domain;
using Marquee.Catalog.Persistence;
using Marquee.Common.Contracts;
using Marquee.Common.Messaging;

namespace Marquee.Catalog.Features;

internal sealed class ShowtimeScheduler(CatalogDbContext db)
{
    /// <summary>
    /// Adds a showtime and, in the same transaction, the event that tells Booking to put its seats
    /// on sale. Either both are stored or neither is.
    /// </summary>
    public Showtime Schedule(Movie movie, Hall hall, DateTimeOffset startsAt, decimal basePrice)
    {
        var showtime = new Showtime
        {
            MovieId = movie.Id,
            HallId = hall.Id,
            StartsAt = startsAt.ToUniversalTime(),
            BasePrice = basePrice,
        };

        db.Showtimes.Add(showtime);
        db.Publish(new ShowtimeScheduled(
            showtime.Id, movie.Title, hall.Name, showtime.StartsAt, hall.PriceSeats(basePrice)));
        return showtime;
    }
}
