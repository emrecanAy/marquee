using System.Security.Claims;

namespace Marquee.Common.Web;

/// <summary>
/// The claims of a Marquee access token. Every visitor has a session, signed in or not; a signed-in
/// visitor also has an account.
/// </summary>
public static class SessionClaims
{
    /// <summary>The visitor's session: what owns a waiting-room place and the seats picked in this browser.</summary>
    public const string SessionId = "sid";

    /// <summary>The account, when the visitor has signed in.</summary>
    public const string AccountId = "sub";

    public const string Name = "name";
    public const string Role = "role";

    /// <summary>The account's session version when the token was issued; tokens of an older version are refused.</summary>
    public const string SessionVersion = "sv";

    /// <summary>Present while the account still has a password that someone else chose.</summary>
    public const string PasswordChangeRequired = "pwd_change";

    /// <summary>The visitor behind the request. Only valid on endpoints that require a session.</summary>
    public static Guid GetSessionId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(SessionId)
            ?? throw new InvalidOperationException("The request carries no session."));

    public static Guid? GetAccountId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(AccountId), out var accountId) ? accountId : null;

    /// <summary>Who tickets and held seats belong to: this browser's session and, once signed in, the account.</summary>
    public static TicketOwner GetTicketOwner(this ClaimsPrincipal user) => new(user.GetSessionId(), user.GetAccountId());
}

/// <summary>
/// The owner of a reservation. Seats picked as a guest belong to the session; once the visitor signs
/// in they also belong to the account, which is what shows them on every device of that account.
/// </summary>
public readonly record struct TicketOwner(Guid SessionId, Guid? AccountId);

public static class Roles
{
    /// <summary>Buys tickets with an account instead of as a guest.</summary>
    public const string Customer = "customer";

    /// <summary>Plans the programme: which film plays in which hall when.</summary>
    public const string Programmer = "programmer";

    /// <summary>Reads the audit trail; changes nothing.</summary>
    public const string Auditor = "auditor";

    /// <summary>Everything staff can do, plus managing the staff accounts.</summary>
    public const string Admin = "admin";

    public static readonly IReadOnlyList<string> Staff = [Programmer, Auditor, Admin];

    public static readonly IReadOnlyList<string> All = [Customer, Programmer, Auditor, Admin];
}

public static class Policies
{
    /// <summary>Any visitor with a session, guest or signed in, who is not stuck on a temporary password.</summary>
    public const string Visitor = "visitor";

    /// <summary>A signed-in account that has chosen its own password.</summary>
    public const string Account = "account";

    /// <summary>Any valid session, including an account still on a temporary password.</summary>
    public const string AnySession = "any-session";
}
