using System.Security.Claims;
using System.Threading.RateLimiting;
using Marquee.Common.Persistence;
using Marquee.Common.Security;
using Marquee.Common.Web;
using Marquee.Identity.Auth;
using Marquee.Identity.Domain;
using Marquee.Identity.Endpoints;
using Marquee.Identity.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marquee.Identity;

/// <summary>
/// Who is at the other end of a request: a guest browser session, a customer account, or a member of
/// staff. Issues and checks every token, and decides the authorization policies the other modules use.
/// </summary>
public static class IdentityModule
{
    /// <summary>Shared by every demo account, which is fine for a demo and only for a demo.</summary>
    internal const string DemoPassword = "marquee-demo-2026";

    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddOptions<IdentityOptions>().BindConfiguration("Identity").ValidateDataAnnotations().ValidateOnStart();
        services.AddModuleDbContext<IdentityDbContext>(IdentityDbContext.Schema);
        services.AddMemoryCache();
        services.AddSingleton<IPasswordHasher<Account>, PasswordHasher<Account>>();
        services.AddSingleton<AccessTokens>();
        services.AddSingleton<Secrets>();
        services.AddSingleton<SessionCheck>();
        services.AddScoped<Sessions>();
        services.AddScoped<SignIn>();
        services.AddPermissions(StaffEndpoints.ManageStaff);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<IdentityOptions>>((jwt, identity) =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = AccessTokens.Validation(identity.Value, IdentityOptions.ApiAudience);
                jwt.Events = new JwtBearerEvents
                {
                    // For an account, a valid signature is not enough: the account must still be active
                    // and the token must come from its current session version.
                    OnTokenValidated = async context =>
                    {
                        var user = context.Principal!;
                        if (user.FindFirstValue(SessionClaims.AccountId) is not { } accountId)
                        {
                            return;
                        }

                        var check = context.HttpContext.RequestServices.GetRequiredService<SessionCheck>();
                        if (!await check.IsCurrentAsync(
                                accountId, user.FindFirstValue(SessionClaims.SessionVersion), context.HttpContext.RequestAborted))
                        {
                            context.Fail("The session behind this token has ended.");
                        }
                    },
                };
            });

        var anySession = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim(SessionClaims.SessionId).Build();
        var visitor = new AuthorizationPolicyBuilder()
            .Combine(anySession)
            .RequireAssertion(context => !context.User.HasClaim(c => c.Type == SessionClaims.PasswordChangeRequired))
            .Build();
        services.AddAuthorizationBuilder()
            // Endpoints that just say "RequireAuthorization()" get a visitor with a session.
            .SetDefaultPolicy(visitor)
            // Endpoints that say nothing still need a signed token; anonymous ones must opt out explicitly.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.AnySession, anySession)
            .AddPolicy(Policies.Visitor, visitor)
            .AddPolicy(Policies.Account, policy => policy.Combine(visitor).RequireClaim(SessionClaims.AccountId));

        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(AuthEndpoints.SignInRateLimit, http => PerAddress(
                http, http.RequestServices.GetRequiredService<IOptions<IdentityOptions>>().Value.SignInRequestsPerMinute));
            limiter.AddPolicy(AuthEndpoints.GuestRateLimit, http => PerAddress(
                http, http.RequestServices.GetRequiredService<IOptions<IdentityOptions>>().Value.GuestSessionsPerMinute));
        });

        return services;
    }

    public static void MapIdentityEndpoints(this IEndpointRouteBuilder api)
    {
        AuthEndpoints.Map(api);
        AccountEndpoints.Map(api);
        StaffEndpoints.Map(api);
    }

    /// <summary>Migrates the schema, makes sure the configured administrator exists and, for a demo, adds a few accounts.</summary>
    public static async Task MigrateIdentityAsync(this IServiceProvider services, bool seedDemoData, CancellationToken ct)
    {
        var db = services.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync(ct);

        var options = services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var hasher = services.GetRequiredService<IPasswordHasher<Account>>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();

        if (!string.IsNullOrWhiteSpace(options.AdminEmail) && !string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            if (PasswordPolicy.Check(options.AdminPassword, options.AdminEmail) is { IsSuccess: false } weak)
            {
                throw new InvalidOperationException($"Identity:AdminPassword is not accepted: {weak.Error.Message}");
            }

            await EnsureAsync(db, hasher, options.AdminEmail, "Sinema Yöneticisi", Roles.Admin, options.AdminPassword, now, ct);
        }

        if (seedDemoData)
        {
            await EnsureAsync(db, hasher, "programci@marquee.local", "Defne Uçar", Roles.Programmer, DemoPassword, now, ct);
            await EnsureAsync(db, hasher, "denetci@marquee.local", "Kerem Aslan", Roles.Auditor, DemoPassword, now, ct);
            await EnsureAsync(db, hasher, "seyirci@marquee.local", "Ada Yılmaz", Roles.Customer, DemoPassword, now, ct);
        }
    }

    private static async Task EnsureAsync(
        IdentityDbContext db, IPasswordHasher<Account> hasher, string email, string name, string role, string password,
        DateTimeOffset now, CancellationToken ct)
    {
        if (await db.Accounts.AnyAsync(account => account.Email == Account.NormalizeEmail(email), ct))
        {
            return;
        }

        var account = Account.Create(email, name, role, now);
        account.SetPassword(hasher.HashPassword(account, password));
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
    }

    private static RateLimitPartition<string> PerAddress(HttpContext http, int perMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) });
}
