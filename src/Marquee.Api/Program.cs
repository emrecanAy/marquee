using System.Threading.RateLimiting;
using Marquee.Api.Infrastructure;
using Marquee.Api.Sessions;
using Marquee.Audit;
using Marquee.Booking;
using Marquee.Catalog;
using Marquee.Common.Web;
using Marquee.Payments;
using Microsoft.AspNetCore.RateLimiting;
using Scalar.AspNetCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

services.AddProblemDetails();
services.AddExceptionHandler<ConcurrencyConflictHandler>();
services.AddOpenApi();
services.AddSingleton(TimeProvider.System);
services.AddSingleton<IConnectionMultiplexer>(provider => ConnectionMultiplexer.Connect(
    provider.GetRequiredService<IConfiguration>().GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis is not configured.")));

services.AddSessions();
services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Per session, so one impatient client hammering "hold" cannot starve the others.
    limiter.AddPolicy(RateLimitPolicies.SeatHolds, context => RateLimitPartition.GetTokenBucketLimiter(
        context.User.FindFirst(SessionClaims.SessionId)?.Value ?? "anonymous",
        _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10,
            TokensPerPeriod = 5,
            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
            QueueLimit = 0,
        }));
});

services.AddCatalogModule();
services.AddBookingModule();
services.AddPaymentsModule();
services.AddAuditModule();

services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis");

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference("/docs");
app.MapHealthChecks("/health");

var api = app.MapGroup("/api");
api.MapSessionEndpoints();
api.MapCatalogEndpoints();
api.MapBookingEndpoints();
api.MapPaymentsEndpoints();
api.MapAuditEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await app.InitializeDatabaseAsync();
}

app.Run();
