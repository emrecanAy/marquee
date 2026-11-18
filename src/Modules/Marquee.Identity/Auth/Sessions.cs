using Marquee.Common.Contracts;
using Marquee.Common.Results;
using Marquee.Identity.Domain;
using Marquee.Identity.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marquee.Identity.Auth;

/// <param name="SessionId">The visitor session the seats and the waiting-room place belong to.</param>
/// <param name="SignedIn">False for a guest.</param>
/// <param name="MustChangePassword">The token is good for changing the password and nothing else.</param>
internal sealed record SessionResponse(
    string AccessToken, DateTimeOffset ExpiresAt, Guid SessionId, bool SignedIn, bool MustChangePassword);

/// <summary>
/// Browser sessions, guest and signed-in alike. The access token goes to the page in the response body
/// and lives only in memory there; the refresh token sits in an HttpOnly, SameSite=Strict cookie that
/// page scripts cannot read and other sites cannot make the browser send.
/// </summary>
internal sealed class Sessions(
    IdentityDbContext db, AccessTokens tokens, SessionCheck check, IGuestReservations guestReservations,
    IOptions<IdentityOptions> options, TimeProvider time, ILogger<Sessions> logger)
{
    private IdentityOptions Options => options.Value;

    /// <summary>A guest session for a new visitor. Seats can be picked and paid for without an account.</summary>
    public Task<SessionResponse> StartGuestAsync(HttpContext http, CancellationToken ct) =>
        StartAsync(Guid.CreateVersion7(), account: null, http, ct);

    /// <summary>
    /// Signs an account in on this browser. A guest session is carried over: the same session id, so
    /// the place in a waiting room and the seats picked a moment ago are still there, and everything the
    /// guest held or bought now belongs to the account too.
    /// </summary>
    /// <param name="keepSessionId">Continue this visitor session, e.g. after a password change on the same browser.</param>
    public async Task<SessionResponse> StartForAccountAsync(
        Account account, HttpContext http, CancellationToken ct, Guid? keepSessionId = null)
    {
        var guest = await PresentedTokenAsync(http, ct);
        var sessionId = keepSessionId ?? Guid.CreateVersion7();
        if (guest is { AccountId: null } && IsLive(guest, time.GetUtcNow()))
        {
            sessionId = guest.SessionId;
            await RevokeFamilyAsync(guest.FamilyId, ct);
            await guestReservations.AttachToAccountAsync(sessionId, account.Id, ct);
        }

        account.RecordSignIn(time.GetUtcNow());
        return await StartAsync(sessionId, account, http, ct);
    }

    /// <summary>
    /// Exchanges the cookie's refresh token for a new one and a fresh access token. A token that was
    /// already exchanged has been copied: the whole session ends, for the thief and the owner alike.
    /// </summary>
    public async Task<Result<SessionResponse>> RefreshAsync(HttpContext http, CancellationToken ct)
    {
        var token = await PresentedTokenAsync(http, ct);
        var now = time.GetUtcNow();
        if (token is null || !IsLive(token, now))
        {
            return SignedOut(http);
        }

        // Claim the token: of two requests presenting it, only one can set "replaced_at".
        var claimed = await db.SessionTokens
            .Where(t => t.Id == token.Id && t.ReplacedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.ReplacedAt, now), ct);
        if (claimed == 0)
        {
            logger.LogWarning("Refresh token reuse in session family {FamilyId}; ending it", token.FamilyId);
            await RevokeFamilyAsync(token.FamilyId, ct);
            return SignedOut(http, "session_revoked");
        }

        Account? account = null;
        if (token.AccountId is { } accountId)
        {
            account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId, ct);
            if (account is null || !account.IsActive || account.SessionVersion != token.SessionVersion)
            {
                await RevokeFamilyAsync(token.FamilyId, ct);
                return SignedOut(http);
            }
        }

        var next = Secrets.NewToken();
        db.SessionTokens.Add(token.Successor(Secrets.Hash(next), now, Options.RefreshTokenLifetime));
        await db.SaveChangesAsync(ct);

        SetCookie(http, next, now + Options.RefreshTokenLifetime);
        return Respond(token.SessionId, account);
    }

    /// <summary>Ends the session the cookie belongs to. Safe to call without one.</summary>
    public async Task EndAsync(HttpContext http, CancellationToken ct)
    {
        if (await PresentedTokenAsync(http, ct) is { } token)
        {
            await RevokeFamilyAsync(token.FamilyId, ct);
        }

        ClearCookie(http);
    }

    public async Task<Guid?> CurrentFamilyAsync(HttpContext http, CancellationToken ct) =>
        (await PresentedTokenAsync(http, ct))?.FamilyId;

    public Task RevokeFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.SessionTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, now), ct);
    }

    /// <summary>Signs the account out on every device: after a new password, a new role, a deactivation.</summary>
    public async Task RevokeAllAsync(Guid accountId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await db.SessionTokens
            .Where(t => t.AccountId == accountId && t.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, now), ct);
        check.Forget(accountId);
    }

    private async Task<SessionResponse> StartAsync(Guid sessionId, Account? account, HttpContext http, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var token = Secrets.NewToken();
        db.SessionTokens.Add(SessionToken.StartFamily(
            sessionId, account?.Id, account?.SessionVersion ?? 0, Secrets.Hash(token), http.Request.Headers.UserAgent.ToString(),
            now, Options.RefreshTokenLifetime));
        await db.SaveChangesAsync(ct);
        if (account is not null)
        {
            check.Forget(account.Id);
        }

        SetCookie(http, token, now + Options.RefreshTokenLifetime);
        return Respond(sessionId, account);
    }

    private async Task<SessionToken?> PresentedTokenAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Request.Cookies[Options.SessionCookieName] is not { Length: > 0 } presented)
        {
            return null;
        }

        var hash = Secrets.Hash(presented);
        return await db.SessionTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
    }

    private bool IsLive(SessionToken token, DateTimeOffset now) =>
        token.RevokedAt is null && token.ExpiresAt > now
        && (token.AccountId is null || now - token.FamilyStartedAt <= Options.SessionMaxAge);

    private SessionResponse Respond(Guid sessionId, Account? account)
    {
        var (accessToken, expiresAt) = tokens.Issue(sessionId, account);
        return new SessionResponse(accessToken, expiresAt, sessionId, account is not null, account?.MustChangePassword ?? false);
    }

    private Error SignedOut(HttpContext http, string code = "signed_out")
    {
        ClearCookie(http);
        return Error.Unauthorized(code, "The session has ended.");
    }

    private void SetCookie(HttpContext http, string token, DateTimeOffset expires)
    {
        // A failed resume earlier in the same request may have queued a deletion; the new cookie replaces it.
        var prefix = Options.SessionCookieName + "=";
        var headers = http.Response.Headers;
        headers.SetCookie = new(headers.SetCookie.Where(c => c is not null && !c.StartsWith(prefix, StringComparison.Ordinal)).ToArray());
        http.Response.Cookies.Append(Options.SessionCookieName, token, CookieOptions(expires));
    }

    private void ClearCookie(HttpContext http) =>
        http.Response.Cookies.Delete(Options.SessionCookieName, CookieOptions(expires: null));

    /// <summary>Only the auth endpoints receive the cookie; no other request carries it.</summary>
    private CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = Options.SecureCookies,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = expires,
        IsEssential = true,
    };
}
