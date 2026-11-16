using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Marquee.Common.Messaging;

public static class MessagingExtensions
{
    /// <summary>Queues an event for delivery. Nothing is sent unless the surrounding SaveChanges commits.</summary>
    public static void Publish<TEvent>(this DbContext db, TEvent integrationEvent)
        where TEvent : IIntegrationEvent =>
        db.Set<OutboxMessage>().Add(OutboxMessage.From(integrationEvent));

    public static ModelBuilder AddOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable("outbox_messages");
            outbox.Property(m => m.Id).ValueGeneratedNever();
            outbox.Property(m => m.Type).HasMaxLength(200);
            outbox.Property(m => m.Payload).HasColumnType("jsonb");
            outbox.HasIndex(m => m.OccurredAt).HasFilter("processed_at IS NULL");
        });
        return modelBuilder;
    }

    /// <summary>Starts the background delivery of a module's outbox.</summary>
    public static IServiceCollection AddOutbox<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.TryAddSingleton<IntegrationEventDispatcher>();
        services.AddOptions<OutboxOptions>().BindConfiguration("Outbox");
        services.AddHostedService<OutboxProcessor<TContext>>();
        return services;
    }

    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.AddScoped<THandler>();
        services.Configure<IntegrationEventRoutes>(routes => routes.Add<TEvent, THandler>());
        return services;
    }
}
