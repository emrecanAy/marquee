using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marquee.Common.Messaging;

public sealed class IntegrationEventDispatcher(IServiceScopeFactory scopes, IOptions<IntegrationEventRoutes> routes)
{
    /// <summary>Runs every subscribed handler in its own scope, so each one commits its own unit of work.</summary>
    public async Task DispatchAsync(string eventType, string payload, CancellationToken ct)
    {
        foreach (var route in routes.Value.For(eventType))
        {
            await using var scope = scopes.CreateAsyncScope();
            await route(scope.ServiceProvider, payload, ct);
        }
    }
}
