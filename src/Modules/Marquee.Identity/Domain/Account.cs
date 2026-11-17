using Marquee.Common.Results;
using Marquee.Common.Web;

namespace Marquee.Identity.Domain;

/// <summary>A person who signs in: a customer who registered, or a member of staff an administrator added.</summary>
internal sealed class Account
{
    private Account()
    {
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = null!;

    public string FullName { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public string Role { get; private set; } = null!;

    public bool IsActive { get; private set; }

    /// <summary>
    /// Raised when access is taken away or changes: a new password, a new role, deactivation. Access
    /// tokens carry the version they were issued with and are refused once it moves on.
    /// </summary>
    public int SessionVersion { get; private set; }

    /// <summary>Set while the account signs in with a password someone else chose.</summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>The authenticator secret, encrypted; present once two-step sign-in is on.</summary>
    public string? TotpSecret { get; private set; }

    public string? PendingTotpSecret { get; private set; }

    /// <summary>The last 30-second step a code was accepted for; a code is never accepted twice.</summary>
    public long? TotpLastStep { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastSignInAt { get; private set; }

    public bool TwoStepEnabled => TotpSecret is not null;

    public bool IsStaff => Roles.Staff.Contains(Role);

    public static Account Create(string email, string fullName, string role, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        Email = NormalizeEmail(email),
        FullName = fullName.Trim(),
        Role = role,
        IsActive = true,
        SessionVersion = 1,
        CreatedAt = now,
    };

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public void SetPassword(string hash)
    {
        PasswordHash = hash;
        MustChangePassword = false;
        SessionVersion++;
    }

    public void SetTemporaryPassword(string hash)
    {
        PasswordHash = hash;
        MustChangePassword = true;
        SessionVersion++;
    }

    public void RehashPassword(string hash) => PasswordHash = hash;

    public void RecordSignIn(DateTimeOffset now) => LastSignInAt = now;

    public void Rename(string fullName) => FullName = fullName.Trim();

    /// <summary>Staff roles only: a customer account is never turned into staff, nor the other way round.</summary>
    public Result ChangeStaffRole(string role)
    {
        if (!IsStaff || !Roles.Staff.Contains(role))
        {
            return Error.Validation("invalid_role", "The role is programmer, auditor or admin.");
        }

        if (role != Role)
        {
            Role = role;
            SessionVersion++;
        }

        return Result.Success;
    }

    public void SetActive(bool isActive)
    {
        if (isActive != IsActive)
        {
            IsActive = isActive;
            SessionVersion++;
        }
    }

    public void BeginTwoStepSetup(string protectedSecret) => PendingTotpSecret = protectedSecret;

    public void EnableTwoStep(long acceptedStep)
    {
        TotpSecret = PendingTotpSecret;
        PendingTotpSecret = null;
        TotpLastStep = acceptedStep;
    }

    public void DisableTwoStep()
    {
        TotpSecret = null;
        PendingTotpSecret = null;
        TotpLastStep = null;
    }

    public bool AcceptTotpStep(long step)
    {
        if (step <= TotpLastStep)
        {
            return false;
        }

        TotpLastStep = step;
        return true;
    }
}
