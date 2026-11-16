using System.ComponentModel.DataAnnotations;

namespace Marquee.Api.Sessions;

public sealed class SessionOptions
{
    public const string Issuer = "marquee";

    /// <summary>HMAC key that signs session tokens. Must be supplied per environment; there is no default.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = null!;

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(14);
}
