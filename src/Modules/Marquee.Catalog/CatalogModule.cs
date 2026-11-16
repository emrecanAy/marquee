using Marquee.Catalog.Features;
using Marquee.Catalog.Persistence;
using Marquee.Common.Messaging;
using Marquee.Common.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marquee.Catalog;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<CatalogDbContext>(CatalogDbContext.Schema);
        services.AddOutbox<CatalogDbContext>();
        services.AddScoped<ShowtimeScheduler>();
        services.AddScoped<DemoProgramme>();
        return services;
    }

    public static void MapCatalogEndpoints(this IEndpointRouteBuilder api) => CatalogEndpoints.Map(api);

    public static async Task MigrateCatalogAsync(this IServiceProvider services, bool seedDemoData, CancellationToken ct)
    {
        await services.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(ct);
        if (seedDemoData)
        {
            await services.GetRequiredService<DemoProgramme>().EnsureAsync(ct);
        }
    }
}
