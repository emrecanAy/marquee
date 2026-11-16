using Marquee.Audit;
using Marquee.Booking;
using Marquee.Catalog;
using Marquee.Payments;

namespace Marquee.Api.Infrastructure;

internal static class DatabaseInitializer
{
    /// <summary>
    /// Applies each module's migrations when the application starts. That suits local runs, tests
    /// and a single-instance deployment; with several instances the migrations should run once
    /// as a deployment step instead (see docs/deployment.md).
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app, CancellationToken ct = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seedDemoData = app.Configuration.GetValue("Database:SeedDemoData", false);

        await services.MigrateBookingAsync(ct);
        await services.MigratePaymentsAsync(ct);
        await services.MigrateAuditAsync(ct);
        await services.MigrateCatalogAsync(seedDemoData, ct);
    }
}
