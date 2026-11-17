using System.Globalization;
using System.Security.Claims;
using System.Text;
using Marquee.Common.Web;
using Marquee.Identity.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Marquee.Identity.Auth;

/// <summary>Short-lived access tokens, and the five-minute ticket between the password and the authenticator code.</summary>
internal sealed class AccessTokens(IOptions<IdentityOptions> options, TimeProvider time)
{
    private static readonly TimeSpan TwoStepTicketLifetime = TimeSpan.FromMinutes(5);

    /// <param name="account">Empty for a guest: the token then names only the browser session.</param>
    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid sessionId, Account? account)
    {
        var claims = new List<Claim>
        {
            new(SessionClaims.SessionId, sessionId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        if (account is not null)
        {
            claims.Add(new Claim(SessionClaims.AccountId, account.Id.ToString()));
            claims.Add(new Claim(SessionClaims.Name, account.FullName));
            claims.Add(new Claim(SessionClaims.Role, account.Role));
            claims.Add(new Claim(SessionClaims.SessionVersion, account.SessionVersion.ToString(CultureInfo.InvariantCulture)));
            if (account.MustChangePassword)
            {
                claims.Add(new Claim(SessionClaims.PasswordChangeRequired, "true"));
            }
        }

        var expiresAt = time.GetUtcNow() + options.Value.AccessTokenLifetime;
        return (Create(claims, IdentityOptions.ApiAudience, expiresAt), expiresAt);
    }

    public string IssueTwoStepTicket(Account account) => Create(
        [
            new(SessionClaims.AccountId, account.Id.ToString()),
            new(SessionClaims.SessionVersion, account.SessionVersion.ToString(CultureInfo.InvariantCulture)),
        ],
        IdentityOptions.TwoStepAudience,
        time.GetUtcNow() + TwoStepTicketLifetime);

    public async Task<(Guid AccountId, int SessionVersion)?> ReadTwoStepTicketAsync(string? ticket)
    {
        if (string.IsNullOrEmpty(ticket))
        {
            return null;
        }

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(ticket, Validation(options.Value, IdentityOptions.TwoStepAudience));
        if (!result.IsValid
            || !result.Claims.TryGetValue(SessionClaims.AccountId, out var accountClaim)
            || !result.Claims.TryGetValue(SessionClaims.SessionVersion, out var versionClaim)
            || !Guid.TryParse(accountClaim as string, out var accountId)
            || !int.TryParse(versionClaim as string, CultureInfo.InvariantCulture, out var version))
        {
            return null;
        }

        return (accountId, version);
    }

    /// <summary>Our issuer, the given audience, HS256 only, not expired.</summary>
    public static TokenValidationParameters Validation(IdentityOptions options, string audience) => new()
    {
        ValidIssuer = IdentityOptions.Issuer,
        ValidAudience = audience,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = SessionClaims.Name,
        RoleClaimType = SessionClaims.Role,
    };

    private string Create(IEnumerable<Claim> claims, string audience, DateTimeOffset expiresAt) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = IdentityOptions.Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = time.GetUtcNow().UtcDateTime,
            NotBefore = time.GetUtcNow().UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.SigningKey)), SecurityAlgorithms.HmacSha256),
        });
}
