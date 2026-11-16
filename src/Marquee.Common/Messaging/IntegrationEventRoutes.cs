using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Marquee.Common.Messaging;

/// <summary>Maps a stored event type name to the handlers subscribed to it.</summary>
public sealed class IntegrationEventRoutes
{
    private readonly Dictionary<string, List<Route>> _routes = [];

    internal delegate Task Route(IServiceProvider services, string payload, CancellationToken ct);

    public static string NameOf<TEvent>()
        where TEvent : IIntegrationEvent => typeof(TEvent).FullName!;

    public void Add<TEvent, THandler>()
        where TEvent : IIntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>
    {
        var name = NameOf<TEvent>();
        if (!_routes.TryGetValue(name, out var routes))
        {
            _routes[name] = routes = [];
        }

        routes.Add((services, payload, ct) =>
        {
            var integrationEvent = JsonSerializer.Deserialize<TEvent>(payload, OutboxMessage.Json)
                ?? throw new InvalidOperationException($"Outbox payload for '{name}' is empty.");
            return services.GetRequiredService<THandler>().HandleAsync(integrationEvent, ct);
        });
    }

    internal IReadOnlyList<Route> For(string eventType) =>
        _routes.TryGetValue(eventType, out var routes) ? routes : [];
}
