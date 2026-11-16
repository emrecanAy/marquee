using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Marquee.Common.Persistence;

public static class ModuleDbContextExtensions
{
    /// <summary>
    /// Registers a module's DbContext. All modules share one database but each owns a schema,
    /// including its own migration history, so a module can later move out with its tables.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : DbContext =>
        services.AddDbContext<TContext>((provider, options) => options
            .UseNpgsql(
                provider.GetRequiredService<IConfiguration>().GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention());

    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
