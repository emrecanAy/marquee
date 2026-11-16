using System.Security.Claims;

namespace Marquee.Common.Web;

public static class SessionClaims
{
    public const string SessionId = "sub";

    /// <summary>The visitor behind the request. Only valid on endpoints that require authorization.</summary>
    public static Guid GetSessionId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(SessionId)
            ?? throw new InvalidOperationException("The request carries no session."));
}
