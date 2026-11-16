using Marquee.Booking.Admission;
using Marquee.Tests.Support;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Marquee.Tests;

[Collection(AppCollection.Name)]
public sealed class WaitingRoomTests(MarqueeApp app) : IAsyncLifetime
{
    private readonly ManualTime _time = new();
    private readonly Guid _screeningId = Guid.NewGuid();

    // A Redis database of its own: the application's dispatcher (real clock, different capacity)
    // sweeps database 0 and must not touch the queues these tests build by hand.
    private ConnectionMultiplexer _redis = null!;

    public async Task InitializeAsync()
    {
        _redis = await ConnectionMultiplexer.ConnectAsync($"{app.RedisConnectionString},defaultDatabase=1");
        await _redis.GetDatabase().KeyDeleteAsync("waiting-room:screenings");
    }

    public Task DisposeAsync() => _redis.DisposeAsync().AsTask();

    [Fact]
    public async Task Visitors_beyond_capacity_wait_and_are_admitted_in_arrival_order()
    {
        var room = RoomWithCapacity(2);
        var (ada, ben, cem, defne) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(QueueState.Admitted, (await JoinAsync(room, ada)).State);
        Assert.Equal(QueueState.Admitted, (await JoinAsync(room, ben)).State);
        Assert.Equal(new QueueTicket(QueueState.Waiting, 1, null), await JoinAsync(room, cem));
        Assert.Equal(new QueueTicket(QueueState.Waiting, 2, null), await JoinAsync(room, defne));

        await room.LeaveAsync(_screeningId, ada);
        Assert.Equal(1, await room.AdmitWaitingAsync());

        Assert.Equal(QueueState.Admitted, (await room.GetAsync(_screeningId, cem)).State);
        Assert.Equal(new QueueTicket(QueueState.Waiting, 1, null), await room.GetAsync(_screeningId, defne));
    }

    [Fact]
    public async Task Joining_twice_does_not_lose_the_place_in_line()
    {
        var room = RoomWithCapacity(1);
        var (ada, ben, cem) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await JoinAsync(room, ada);
        await JoinAsync(room, ben);
        await JoinAsync(room, cem);

        var again = await JoinAsync(room, ben);

        Assert.Equal(new QueueTicket(QueueState.Waiting, 1, null), again);
    }

    [Fact]
    public async Task A_free_place_is_not_handed_to_a_newcomer_while_others_are_waiting()
    {
        var room = RoomWithCapacity(1);
        var (ada, ben, cem) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await JoinAsync(room, ada);
        await JoinAsync(room, ben);
        await room.LeaveAsync(_screeningId, ada);

        // The dispatcher has not run yet, so a place is free, but Ben was here first.
        var newcomer = await JoinAsync(room, cem);

        Assert.Equal(new QueueTicket(QueueState.Waiting, 2, null), newcomer);
    }

    [Fact]
    public async Task A_waiting_visitor_who_stopped_polling_is_skipped()
    {
        var room = RoomWithCapacity(1);
        var (ada, gone, cem) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await JoinAsync(room, ada);
        await JoinAsync(room, gone);
        await JoinAsync(room, cem);

        _time.Advance(TimeSpan.FromSeconds(30));
        await room.GetAsync(_screeningId, cem);
        await room.LeaveAsync(_screeningId, ada);
        await room.AdmitWaitingAsync();

        Assert.Equal(QueueState.Admitted, (await room.GetAsync(_screeningId, cem)).State);
        Assert.Equal(QueueState.None, (await room.GetAsync(_screeningId, gone)).State);
    }

    [Fact]
    public async Task An_admission_that_is_not_used_runs_out_and_frees_the_place()
    {
        var room = RoomWithCapacity(1);
        var (idle, next) = (Guid.NewGuid(), Guid.NewGuid());
        await JoinAsync(room, idle);

        _time.Advance(TimeSpan.FromMinutes(11));

        Assert.Equal(QueueState.Admitted, (await JoinAsync(room, next)).State);
        Assert.False(await room.IsAdmittedAsync(_screeningId, idle));
    }

    private WaitingRoom RoomWithCapacity(int capacity) =>
        new(_redis, Options.Create(new WaitingRoomOptions { Capacity = capacity }), _time);

    // Arrivals are a second apart so their order is unambiguous.
    private async Task<QueueTicket> JoinAsync(WaitingRoom room, Guid sessionId)
    {
        _time.Advance(TimeSpan.FromSeconds(1));
        return await room.JoinAsync(_screeningId, sessionId);
    }
}
