using System.Text.Json;
using Marquee.Common.Persistence;
using Marquee.Common.Security;
using Marquee.Common.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marquee.Audit;

public static class AuditModule
{
    /// <summary>The trail shows every reservation and payment change, so it is for auditors only.</summary>
    internal static readonly Permission ReadTrail = new("audit.read", "Değişiklik kaydını okur", Roles.Auditor);

    internal sealed record ChangeResponse(
        long Id, string Table, string Operation, JsonElement? Before, JsonElement? After, DateTimeOffset CommittedAt);

    internal sealed record AuditTrailResponse(bool Enabled, IReadOnlyList<ChangeResponse> Changes);

    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        services.AddOptions<AuditOptions>().BindConfiguration("Audit");
        services.AddModuleDbContext<AuditDbContext>(AuditDbContext.Schema);
        services.AddHostedService<ChangeEventConsumer>();
        services.AddPermissions(ReadTrail);
        return services;
    }

    public static void MapAuditEndpoints(this IEndpointRouteBuilder api) =>
        api.MapGet("/audit/changes", async (
                string? table, int? take, AuditDbContext db, IOptions<AuditOptions> options, CancellationToken ct) =>
            {
                var changes = await db.ChangeEvents.AsNoTracking()
                    .Where(change => table == null || change.Table == table)
                    .OrderByDescending(change => change.Id)
                    .Take(Math.Clamp(take ?? 50, 1, 200))
                    .ToListAsync(ct);

                return new AuditTrailResponse(options.Value.Enabled, changes.ConvertAll(change => new ChangeResponse(
                    change.Id, change.Table, change.Operation, ToJson(change.Before), ToJson(change.After),
                    change.CommittedAt)));
            })
            .RequirePermission(ReadTrail)
            .WithTags("Audit");

    public static Task MigrateAuditAsync(this IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<AuditDbContext>().Database.MigrateAsync(ct);

    private static JsonElement? ToJson(string? raw) =>
        raw is null ? null : JsonSerializer.Deserialize<JsonElement>(raw);
}
