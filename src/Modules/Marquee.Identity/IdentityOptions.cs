using System.ComponentModel.DataAnnotations;

namespace Marquee.Identity;

public sealed class IdentityOptions
{
    public const string Issuer = "marquee";

    /// <summary>The only audience an access token is accepted for.</summary>
    public const string ApiAudience = "marquee-api";

    /// <summary>The ticket between the password and the authenticator code; never accepted by the API.</summary>
    public const string TwoStepAudience = "marquee-two-step";

    /// <summary>HMAC key that signs access tokens. Supplied per environment; there is no default.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = null!;

    /// <summary>Key that encrypts authenticator secrets at rest. Separate from the signing key; no default.</summary>
    [Required, MinLength(32)]
    public string EncryptionKey { get; set; } = null!;

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long a browser keeps its session (and a guest its held seats and tickets) without visiting.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>The longest a signed-in session lasts; after that the password is asked again.</summary>
    public TimeSpan SessionMaxAge { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Send the session cookie only over HTTPS. Off only for a local run over plain HTTP.</summary>
    public bool SecureCookies { get; set; } = true;

    public int LockoutThreshold { get; set; } = 5;

    public TimeSpan LockoutWindow { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Sign-in and sign-up requests one IP address may make per minute.</summary>
    public int SignInRequestsPerMinute { get; set; } = 10;

    /// <summary>Guest sessions one IP address may open per minute; a browser needs one.</summary>
    public int GuestSessionsPerMinute { get; set; } = 30;

    /// <summary>The first administrator, created at start-up if no account has this e-mail yet.</summary>
    public string? AdminEmail { get; set; }

    /// <summary>Must meet the password policy; the API refuses to start otherwise.</summary>
    public string? AdminPassword { get; set; }

    public string SessionCookieName => SecureCookies ? "__Secure-marquee-session" : "marquee-session";
}
