using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Marquee.Booking.Admission;

/// <summary>
/// Limits how many customers shop one screening at a time and makes everyone else wait in
/// arrival order, so a popular premiere turns into a queue instead of a stampede on the database.
///
/// Per screening there are three sorted sets:
/// <list type="bullet">
/// <item><c>active</c>: admitted sessions, scored by when their admission ends. Expired members are
/// trimmed on every operation, so a customer who walks away frees the place without any counter
/// that could drift.</item>
/// <item><c>queue</c>: waiting sessions, scored by arrival time.</item>
/// <item><c>seen</c>: last poll of each waiting session, used to skip abandoned places in line.</item>
/// </list>
/// Each operation is one Lua script, which Redis runs atomically.
/// </summary>
internal sealed class WaitingRoom(IConnectionMultiplexer redis, IOptions<WaitingRoomOptions> options, TimeProvider time)
{
    private const string ScreeningsWithQueue = "waiting-room:screenings";

    // KEYS: active, queue, seen. ARGV: now, admittedUntil, capacity, session, enqueue (1/0).
    // Returns {state, position, admittedUntil}.
    private const string EnterScript = """
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[1])

        local admittedUntil = redis.call('ZSCORE', KEYS[1], ARGV[4])
        if admittedUntil then
            return {2, 0, tonumber(admittedUntil)}
        end

        local rank = redis.call('ZRANK', KEYS[2], ARGV[4])
        if not rank then
            if ARGV[5] == '0' then
                return {0, 0, 0}
            end
            -- Walk straight in only when nobody is queueing; otherwise late arrivals would overtake the line.
            if redis.call('ZCARD', KEYS[2]) == 0 and redis.call('ZCARD', KEYS[1]) < tonumber(ARGV[3]) then
                redis.call('ZADD', KEYS[1], ARGV[2], ARGV[4])
                return {2, 0, tonumber(ARGV[2])}
            end
            redis.call('ZADD', KEYS[2], ARGV[1], ARGV[4])
            rank = redis.call('ZRANK', KEYS[2], ARGV[4])
        end

        redis.call('ZADD', KEYS[3], ARGV[1], ARGV[4])
        return {1, rank + 1, 0}
        """;

    // KEYS: active, queue, seen. ARGV: now, admittedUntil, capacity, staleBefore.
    // Returns {admitted, stillWaiting}.
    private const string AdmitScript = """
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[1])

        local free = tonumber(ARGV[3]) - redis.call('ZCARD', KEYS[1])
        local admitted = 0
        while free > 0 do
            local head = redis.call('ZPOPMIN', KEYS[2])
            if #head == 0 then
                break
            end
            local session = head[1]
            local lastSeen = redis.call('ZSCORE', KEYS[3], session)
            redis.call('ZREM', KEYS[3], session)
            if lastSeen and tonumber(lastSeen) >= tonumber(ARGV[4]) then
                redis.call('ZADD', KEYS[1], ARGV[2], session)
                free = free - 1
                admitted = admitted + 1
            end
        end
        return {admitted, redis.call('ZCARD', KEYS[2])}
        """;

    private readonly WaitingRoomOptions _options = options.Value;

    /// <summary>Admits the session if there is room, otherwise puts it at the end of the line.</summary>
    public async Task<QueueTicket> JoinAsync(Guid screeningId, Guid sessionId)
    {
        var ticket = await EnterAsync(screeningId, sessionId, enqueue: true);
        if (ticket.State == QueueState.Waiting)
        {
            await redis.GetDatabase().SetAddAsync(ScreeningsWithQueue, screeningId.ToString());
        }

        return ticket;
    }

    /// <summary>Reports where the session stands. For a waiting session this also counts as its heartbeat.</summary>
    public Task<QueueTicket> GetAsync(Guid screeningId, Guid sessionId) =>
        EnterAsync(screeningId, sessionId, enqueue: false);

    public async Task<bool> IsAdmittedAsync(Guid screeningId, Guid sessionId) =>
        (await GetAsync(screeningId, sessionId)).State == QueueState.Admitted;

    /// <summary>Gives the place back, whether the session was waiting or already admitted.</summary>
    public async Task LeaveAsync(Guid screeningId, Guid sessionId)
    {
        var keys = Keys(screeningId);
        var member = (RedisValue)sessionId.ToString();
        var batch = redis.GetDatabase().CreateBatch();
        var removals = keys.Select(key => batch.SortedSetRemoveAsync(key, member)).ToArray();
        batch.Execute();
        await Task.WhenAll(removals);
    }

    /// <summary>Moves waiting sessions into free places, for every screening that has a queue.</summary>
    /// <returns>The number of sessions admitted.</returns>
    public async Task<int> AdmitWaitingAsync()
    {
        var db = redis.GetDatabase();
        var now = time.GetUtcNow();
        var admitted = 0;

        foreach (var screeningId in await db.SetMembersAsync(ScreeningsWithQueue))
        {
            var keys = Keys(Guid.Parse(screeningId.ToString()));
            var result = (long[])(await db.ScriptEvaluateAsync(AdmitScript, keys,
            [
                now.ToUnixTimeMilliseconds(),
                (now + _options.AdmissionWindow).ToUnixTimeMilliseconds(),
                _options.Capacity,
                (now - _options.HeartbeatTimeout).ToUnixTimeMilliseconds(),
            ]))!;
            admitted += (int)result[0];

            // Forget the screening once its line is empty. A customer may join between the script and
            // the SREM, so look again afterwards and put the screening back if that happened.
            if (result[1] == 0)
            {
                await db.SetRemoveAsync(ScreeningsWithQueue, screeningId);
                if (await db.SortedSetLengthAsync(keys[1]) > 0)
                {
                    await db.SetAddAsync(ScreeningsWithQueue, screeningId);
                }
            }
        }

        return admitted;
    }

    private async Task<QueueTicket> EnterAsync(Guid screeningId, Guid sessionId, bool enqueue)
    {
        var now = time.GetUtcNow();
        var result = (long[])(await redis.GetDatabase().ScriptEvaluateAsync(EnterScript, Keys(screeningId),
        [
            now.ToUnixTimeMilliseconds(),
            (now + _options.AdmissionWindow).ToUnixTimeMilliseconds(),
            _options.Capacity,
            sessionId.ToString(),
            enqueue ? 1 : 0,
        ]))!;

        return (QueueState)result[0] switch
        {
            QueueState.Admitted => new QueueTicket(QueueState.Admitted, 0, DateTimeOffset.FromUnixTimeMilliseconds(result[2])),
            QueueState.Waiting => new QueueTicket(QueueState.Waiting, (int)result[1], null),
            _ => new QueueTicket(QueueState.None, 0, null),
        };
    }

    // Hash tag: the three sets of a screening share a cluster slot, as one script needs them all.
    private static RedisKey[] Keys(Guid screeningId) =>
    [
        $"{{waiting-room:{screeningId}}}:active",
        $"{{waiting-room:{screeningId}}}:queue",
        $"{{waiting-room:{screeningId}}}:seen",
    ];
}
