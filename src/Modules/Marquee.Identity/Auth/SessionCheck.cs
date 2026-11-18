using System.Globalization;
using Marquee.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Marquee.Identity.Auth;

/// <summary>
/// Runs for every request with an access token: is the user still active, and is the token from
/// their current session version? This is what makes a password change, a role change or a
/// deactivation take effect at once instead of when the token expires. The answer is cached for a
/// few seconds per user and dropped right away when this instance changes the user.
/// </summary>
internal sealed class SessionCheck(IServiceScopeFactory scopes, IMemoryCache cache)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(20);

    public async Task<bool> IsCurrentAsync(string? userIdClaim, string? versionClaim, CancellationToken ct)
    {
        if (!Guid.TryParse(userIdClaim, out var userId)
            || !int.TryParse(versionClaim, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            return false;
        }

        var current = await cache.GetOrCreateAsync(Key(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            return await db.Accounts.AsNoTracking()
                .Where(account => account.Id == userId && account.IsActive)
                .Select(account => (int?)account.SessionVersion)
                .SingleOrDefaultAsync(ct);
        });

        return current == version;
    }

    public void Forget(Guid userId) => cache.Remove(Key(userId));

    private static string Key(Guid userId) => $"session-version:{userId}";
}
