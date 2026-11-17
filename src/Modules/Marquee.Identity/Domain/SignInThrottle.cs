namespace Marquee.Identity.Domain;

/// <summary>
/// Failed sign-ins per e-mail address, whether or not an account exists for it, so a locked address
/// tells an attacker nothing about which accounts are real.
/// </summary>
internal sealed class SignInThrottle
{
    private SignInThrottle()
    {
    }

    /// <summary>The normalised e-mail address.</summary>
    public string Email { get; private set; } = null!;

    public int Failures { get; private set; }

    public DateTimeOffset WindowStartedAt { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public static SignInThrottle For(string email, DateTimeOffset now) => new() { Email = email, WindowStartedAt = now };

    public bool IsLocked(DateTimeOffset now) => LockedUntil > now;

    public void RecordFailure(DateTimeOffset now, int threshold, TimeSpan window, TimeSpan lockout)
    {
        if (now - WindowStartedAt > window)
        {
            WindowStartedAt = now;
            Failures = 0;
        }

        Failures++;
        if (Failures >= threshold)
        {
            LockedUntil = now + lockout;
            Failures = 0;
            WindowStartedAt = now;
        }
    }
}

/// <summary>A single-use code that signs in when the authenticator app is out of reach. Stored hashed.</summary>
internal sealed class RecoveryCode
{
    private RecoveryCode()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public string CodeHash { get; private set; } = null!;

    public DateTimeOffset? UsedAt { get; private set; }

    public static RecoveryCode For(Guid accountId, string codeHash) =>
        new() { Id = Guid.CreateVersion7(), AccountId = accountId, CodeHash = codeHash };
}
