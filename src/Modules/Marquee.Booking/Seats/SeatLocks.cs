using StackExchange.Redis;

namespace Marquee.Booking.Seats;

/// <summary>
/// The fast gate in front of PostgreSQL. A seat is claimed with <c>SET key owner NX PX ttl</c>:
/// Redis executes it atomically, so of any number of simultaneous requests exactly one gets the
/// key and the rest are turned away in memory without opening a database transaction.
///
/// The lock is an optimisation, not the source of truth. If Redis ever loses a key (failover,
/// eviction), the seat row's status and its xmin version still stop a double sale.
/// </summary>
internal sealed class SeatLocks(IConnectionMultiplexer redis)
{
    // Claims every seat or none. Scripts run single-threaded in Redis, so undoing the partial
    // claim on the first conflict is invisible to other clients.
    private const string AcquireScript = """
        for i, key in ipairs(KEYS) do
            if not redis.call('SET', key, ARGV[1], 'NX', 'PX', ARGV[2]) then
                for j = 1, i - 1 do
                    redis.call('DEL', KEYS[j])
                end
                return i
            end
        end
        return 0
        """;

    // Only the owner may release or extend: a request whose lock already expired must not
    // remove the lock of whoever claimed the seat afterwards.
    private const string ReleaseScript = """
        for _, key in ipairs(KEYS) do
            if redis.call('GET', key) == ARGV[1] then
                redis.call('DEL', key)
            end
        end
        return 0
        """;

    private const string ExtendScript = """
        for _, key in ipairs(KEYS) do
            if redis.call('GET', key) == ARGV[1] then
                redis.call('PEXPIRE', key, ARGV[2])
            end
        end
        return 0
        """;

    /// <returns><c>null</c> when all seats were claimed, otherwise the label of a seat someone else holds.</returns>
    public async Task<string?> TryAcquireAsync(
        Guid screeningId, IReadOnlyList<string> seatLabels, Guid owner, TimeSpan ttl)
    {
        var conflictIndex = (int)await redis.GetDatabase().ScriptEvaluateAsync(
            AcquireScript,
            Keys(screeningId, seatLabels),
            [owner.ToString(), (long)ttl.TotalMilliseconds]);

        return conflictIndex == 0 ? null : seatLabels[conflictIndex - 1];
    }

    public Task ReleaseAsync(Guid screeningId, IReadOnlyList<string> seatLabels, Guid owner) =>
        redis.GetDatabase().ScriptEvaluateAsync(ReleaseScript, Keys(screeningId, seatLabels), [owner.ToString()]);

    public Task ExtendAsync(Guid screeningId, IReadOnlyList<string> seatLabels, Guid owner, TimeSpan ttl) =>
        redis.GetDatabase().ScriptEvaluateAsync(
            ExtendScript, Keys(screeningId, seatLabels), [owner.ToString(), (long)ttl.TotalMilliseconds]);

    // The {braces} are a Redis Cluster hash tag: all seats of a screening live in one slot,
    // which is what allows a single script to touch several of them.
    private static RedisKey[] Keys(Guid screeningId, IReadOnlyList<string> seatLabels) =>
        [.. seatLabels.Select(label => (RedisKey)$"{{seats:{screeningId}}}:{label}")];
}
