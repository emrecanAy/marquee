using System.Threading.RateLimiting;
using Marquee.Api.Infrastructure;
using Marquee.Audit;
using Marquee.Booking;
using Marquee.Catalog;
using Marquee.Identity;
using Marquee.Common.Web;
using Marquee.Payments;
using Microsoft.AspNetCore.HttpOverrides;
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

services.AddIdentityModule();
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

// Behind the web container's nginx the client address arrives in X-Forwarded-For. It is believed only
// from the networks listed here, so a client talking to the API directly cannot choose its own address
// and get round the sign-in rate limit.
services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (http, next) =>
{
    // An API answers with data, never with something to render or frame, and nothing it says is cached.
    var headers = http.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers.CacheControl = "no-store";
    headers["Referrer-Policy"] = "no-referrer";
    await next(http);
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi().AllowAnonymous();
app.MapScalarApiReference("/docs").AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();

var api = app.MapGroup("/api");
api.MapIdentityEndpoints();
api.MapCatalogEndpoints();
api.MapBookingEndpoints();
api.MapPaymentsEndpoints();
api.MapAuditEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await app.InitializeDatabaseAsync();
}

app.Run();
