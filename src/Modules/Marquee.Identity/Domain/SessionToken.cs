namespace Marquee.Identity.Domain;

/// <summary>
/// One link in a browser session's chain of refresh tokens. A session starts as a guest's and may later
/// belong to an account; its <see cref="SessionId"/> stays the same across sign-in, so seats picked as a
/// guest stay picked. Every refresh replaces the token; one presented twice was copied, and the whole
/// session is ended. Only a hash is stored.
/// </summary>
internal sealed class SessionToken
{
    public const int MaxDeviceLength = 200;

    private SessionToken()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>The chain of tokens of one sign-in on one device.</summary>
    public Guid FamilyId { get; private set; }

    /// <summary>The visitor session the access tokens carry; what owns waiting-room places and held seats.</summary>
    public Guid SessionId { get; private set; }

    /// <summary>Empty while the visitor is a guest.</summary>
    public Guid? AccountId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    /// <summary>The account's session version when the family started; a newer version ends it. 0 for guests.</summary>
    public int SessionVersion { get; private set; }

    public string? Device { get; private set; }

    public DateTimeOffset FamilyStartedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ReplacedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public static SessionToken StartFamily(
        Guid sessionId, Guid? accountId, int sessionVersion, string tokenHash, string? device, DateTimeOffset now,
        TimeSpan lifetime)
    {
        var id = Guid.CreateVersion7();
        return new SessionToken
        {
            Id = id,
            FamilyId = id,
            SessionId = sessionId,
            AccountId = accountId,
            TokenHash = tokenHash,
            SessionVersion = sessionVersion,
            Device = device is null ? null : device[..Math.Min(device.Length, MaxDeviceLength)],
            FamilyStartedAt = now,
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };
    }

    public SessionToken Successor(string tokenHash, DateTimeOffset now, TimeSpan lifetime) => new()
    {
        Id = Guid.CreateVersion7(),
        FamilyId = FamilyId,
        SessionId = SessionId,
        AccountId = AccountId,
        TokenHash = tokenHash,
        SessionVersion = SessionVersion,
        Device = Device,
        FamilyStartedAt = FamilyStartedAt,
        CreatedAt = now,
        ExpiresAt = now + lifetime,
    };
}
