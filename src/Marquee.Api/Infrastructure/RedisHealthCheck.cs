using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Marquee.Api.Infrastructure;

internal sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (RedisException ex)
        {
            return HealthCheckResult.Unhealthy("Redis did not answer PING.", ex);
        }
    }
}
