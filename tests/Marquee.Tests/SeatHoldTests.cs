using System.Net;
using Marquee.Booking.Persistence;
using Marquee.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marquee.Tests;

[Collection(AppCollection.Name)]
public sealed class SeatHoldTests(MarqueeApp app)
{
    [Fact]
    public async Task One_hundred_visitors_racing_for_the_same_seat_produce_exactly_one_hold()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        var visitors = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => app.CreateAdmittedVisitorAsync(showtimeId)));

        var responses = await Task.WhenAll(visitors.Select(visitor => visitor.HoldAsync(showtimeId, "A1")));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Equal(99, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task A_multi_seat_hold_is_all_or_nothing()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        var first = await app.CreateAdmittedVisitorAsync(showtimeId);
        var second = await app.CreateAdmittedVisitorAsync(showtimeId);
        await first.HoldSuccessfullyAsync(showtimeId, "A2");

        var response = await second.HoldAsync(showtimeId, "A1", "A2", "A3");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var seats = await second.GetSeatStatusesAsync(showtimeId);
        Assert.Equal("available", seats["A1"]);
        Assert.Equal("available", seats["A3"]);

        // A1 and A3 must not be left locked in Redis by the failed attempt either.
        await second.HoldSuccessfullyAsync(showtimeId, "A1", "A3");
    }

    /// <summary>
    /// Redis is the fast path, not the source of truth. If a lock disappears (failover, eviction),
    /// the seat row in PostgreSQL still refuses a second owner.
    /// </summary>
    [Fact]
    public async Task When_the_redis_lock_is_lost_the_database_still_refuses_a_second_hold()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        var first = await app.CreateAdmittedVisitorAsync(showtimeId);
        var second = await app.CreateAdmittedVisitorAsync(showtimeId);
        await first.HoldSuccessfullyAsync(showtimeId, "B1");

        await app.Redis.GetDatabase().KeyDeleteAsync($"{{seats:{showtimeId}}}:B1");
        var response = await second.HoldAsync(showtimeId, "B1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("seat_taken", await response.ErrorCodeAsync());
    }

    /// <summary>The last line of defence: two transactions that both read the seat as available.</summary>
    [Fact]
    public async Task Of_two_transactions_updating_the_same_seat_version_only_the_first_commits()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        await using var firstScope = app.Services.CreateAsyncScope();
        await using var secondScope = app.Services.CreateAsyncScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var seenByFirst = await firstDb.Seats.SingleAsync(seat => seat.ScreeningId == showtimeId && seat.Label == "A1");
        var seenBySecond = await secondDb.Seats.SingleAsync(seat => seat.ScreeningId == showtimeId && seat.Label == "A1");
        seenByFirst.Hold(Guid.NewGuid());
        seenBySecond.Hold(Guid.NewGuid());

        await firstDb.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync());
    }

    [Fact]
    public async Task Seats_cannot_be_held_without_passing_the_waiting_room()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        var visitor = await app.CreateVisitorAsync();

        var response = await visitor.HoldAsync(showtimeId, "A1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("not_admitted", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task Choosing_again_swaps_the_previous_selection_for_the_new_one()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        var visitor = await app.CreateAdmittedVisitorAsync(showtimeId);
        await visitor.HoldSuccessfullyAsync(showtimeId, "A1", "A2");

        await visitor.HoldSuccessfullyAsync(showtimeId, "A2", "A3");

        var seats = await visitor.GetSeatStatusesAsync(showtimeId);
        Assert.Equal("available", seats["A1"]);
        Assert.Equal("mine", seats["A2"]);
        Assert.Equal("mine", seats["A3"]);
    }

    [Fact]
    public async Task An_expired_hold_puts_its_seats_back_on_sale()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        var first = await app.CreateAdmittedVisitorAsync(showtimeId);
        var second = await app.CreateAdmittedVisitorAsync(showtimeId);
        var reservation = await first.HoldSuccessfullyAsync(showtimeId, "A1");

        await app.ExpireHoldsAsync();

        Assert.Equal("expired", (await first.GetReservationAsync(reservation.Id)).Status);
        await second.HoldSuccessfullyAsync(showtimeId, "A1");
    }

    [Fact]
    public async Task A_cancelled_hold_puts_its_seats_back_on_sale()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        var first = await app.CreateAdmittedVisitorAsync(showtimeId);
        var second = await app.CreateAdmittedVisitorAsync(showtimeId);
        var reservation = await first.HoldSuccessfullyAsync(showtimeId, "A1");

        (await first.DeleteAsync($"/api/reservations/{reservation.Id}")).EnsureSuccessStatusCode();

        await second.HoldSuccessfullyAsync(showtimeId, "A1");
    }
}
