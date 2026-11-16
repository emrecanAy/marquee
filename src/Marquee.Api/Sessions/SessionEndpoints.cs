using System.Security.Claims;
using System.Text;
using Marquee.Common.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Marquee.Api.Sessions;

/// <summary>
/// Guest checkout: a visitor gets an anonymous, signed session instead of an account. The session id
/// is what owns waiting-room places, seat holds and tickets, and because it is signed a client
/// cannot claim someone else's.
/// </summary>
internal static class SessionEndpoints
{
    public sealed record SessionResponse(Guid SessionId, string Token, DateTimeOffset ExpiresAt);

    public static IServiceCollection AddSessions(this IServiceCollection services)
    {
        services.AddOptions<SessionOptions>().BindConfiguration("Sessions").ValidateDataAnnotations().ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<SessionOptions>>((jwt, session) =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = SessionOptions.Issuer,
                    ValidateAudience = false,
                    IssuerSigningKey = SigningKey(session.Value),
                    NameClaimType = SessionClaims.SessionId,
                };
            });
        services.AddAuthorization();
        return services;
    }

    public static void MapSessionEndpoints(this IEndpointRouteBuilder api) =>
        api.MapPost("/sessions", (IOptions<SessionOptions> options, TimeProvider time) =>
            {
                var sessionId = Guid.CreateVersion7();
                var expiresAt = time.GetUtcNow() + options.Value.Lifetime;
                var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
                {
                    Issuer = SessionOptions.Issuer,
                    Subject = new ClaimsIdentity([new Claim(SessionClaims.SessionId, sessionId.ToString())]),
                    Expires = expiresAt.UtcDateTime,
                    SigningCredentials = new SigningCredentials(SigningKey(options.Value), SecurityAlgorithms.HmacSha256),
                });

                return new SessionResponse(sessionId, token, expiresAt);
            })
            .WithTags("Sessions");

    private static SymmetricSecurityKey SigningKey(SessionOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningKey));
}
