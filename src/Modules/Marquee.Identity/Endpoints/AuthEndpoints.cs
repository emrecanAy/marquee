using System.Security.Claims;
using Marquee.Common.Persistence;
using Marquee.Common.Results;
using Marquee.Common.Security;
using Marquee.Common.Web;
using Marquee.Identity.Auth;
using Marquee.Identity.Domain;
using Marquee.Identity.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Identity.Endpoints;

internal static class AuthEndpoints
{
    /// <summary>
    /// Every request to /api/auth must carry it. A form on another site cannot set a custom header, and
    /// a script on another site would need a CORS permission this API never grants, so a request with it
    /// came from Marquee's own pages. SameSite=Strict on the cookie is the first wall.
    /// </summary>
    public const string ClientHeader = "X-Requested-With";
    public const string ClientHeaderValue = "marquee";

    public const string SignInRateLimit = "sign-in";
    public const string GuestRateLimit = "guest-session";

    public sealed record SignInRequest(string? Email, string? Password);

    public sealed record RegisterRequest(string? FullName, string? Email, string? Password);

    /// <summary>Either a session, or a ticket for the second step when the account has two-step sign-in.</summary>
    public sealed record SignInResponse(SessionResponse? Session, string? TwoStepTicket);

    public sealed record SecondStepRequest(string? Ticket, string? Code);

    public sealed record MeResponse(Guid SessionId, AccountResponse? Account, IReadOnlyList<string> Permissions);

    public sealed record AccountResponse(Guid Id, string FullName, string Email, string Role, bool TwoStepEnabled, bool MustChangePassword);

    public static void Map(IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").AllowAnonymous().AddEndpointFilter(RequireClientHeader).WithTags("Auth");

        // The page's first call: resumes the session in the cookie, or opens a guest one.
        auth.MapPost("/session", async (HttpContext http, Sessions sessions, CancellationToken ct) =>
            await sessions.RefreshAsync(http, ct) is { IsSuccess: true } resumed
                ? Results.Ok(resumed.Value)
                : Results.Ok(await sessions.StartGuestAsync(http, ct)))
            .RequireRateLimiting(GuestRateLimit);
        auth.MapPost("/refresh", async (HttpContext http, Sessions sessions, CancellationToken ct) =>
            (await sessions.RefreshAsync(http, ct)).ToHttp());
        auth.MapPost("/register", RegisterAsync).RequireRateLimiting(SignInRateLimit);
        auth.MapPost("/sign-in", SignInAsync).RequireRateLimiting(SignInRateLimit);
        auth.MapPost("/sign-in/second-step", SecondStepAsync).RequireRateLimiting(SignInRateLimit);
        auth.MapPost("/sign-out", async (HttpContext http, Sessions sessions, CancellationToken ct) =>
        {
            await sessions.EndAsync(http, ct);
            return Results.NoContent();
        });

        // Open to an account on a temporary password too: the page needs to know it must ask for a new one.
        api.MapGet("/me", MeAsync).RequireAuthorization(Policies.AnySession).WithTags("Auth");
    }

    /// <summary>
    /// Opens a customer account and signs it in on this browser, keeping what the guest picked. Staff
    /// accounts are never made here; an administrator adds them.
    /// </summary>
    private static async Task<IResult> RegisterAsync(
        RegisterRequest request, HttpContext http, IdentityDbContext db, IPasswordHasher<Account> hasher, Sessions sessions,
        TimeProvider time, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 120
            || request.Email is not { Length: <= 200 } email || !email.Contains('@'))
        {
            return Error.Validation("invalid_registration", "A name and a valid e-mail address are required.").ToProblem();
        }

        if (PasswordPolicy.Check(request.Password, email) is { IsSuccess: false } weak)
        {
            return weak.Error.ToProblem();
        }

        var account = Account.Create(email, request.FullName, Roles.Customer, time.GetUtcNow());
        account.SetPassword(hasher.HashPassword(account, request.Password!));
        db.Accounts.Add(account);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return Error.Conflict("email_in_use", "An account with this e-mail address already exists. Sign in instead.").ToProblem();
        }

        return Results.Ok(new SignInResponse(await sessions.StartForAccountAsync(account, http, ct), null));
    }

    private static async Task<IResult> SignInAsync(
        SignInRequest request, HttpContext http, SignIn signIn, Sessions sessions, AccessTokens tokens, IdentityDbContext db,
        CancellationToken ct)
    {
        var account = await signIn.CheckPasswordAsync(request.Email, request.Password, ct);
        if (!account.IsSuccess)
        {
            return account.Error.ToProblem();
        }

        if (account.Value.TwoStepEnabled)
        {
            await db.SaveChangesAsync(ct);
            return Results.Ok(new SignInResponse(null, tokens.IssueTwoStepTicket(account.Value)));
        }

        var session = await sessions.StartForAccountAsync(account.Value, http, ct);
        return Results.Ok(new SignInResponse(session, null));
    }

    private static async Task<IResult> SecondStepAsync(
        SecondStepRequest request, HttpContext http, SignIn signIn, Sessions sessions, AccessTokens tokens, IdentityDbContext db,
        CancellationToken ct)
    {
        var ticket = await tokens.ReadTwoStepTicketAsync(request.Ticket);
        var account = ticket is null ? null : await db.Accounts.SingleOrDefaultAsync(a => a.Id == ticket.Value.AccountId, ct);
        if (account is null || !account.IsActive || account.SessionVersion != ticket!.Value.SessionVersion || !account.TwoStepEnabled)
        {
            return Error.Unauthorized("ticket_expired", "The sign-in took too long. Start again with the password.").ToProblem();
        }

        var checkedAccount = await signIn.CheckSecondStepAsync(account, request.Code, ct);
        return checkedAccount.IsSuccess
            ? Results.Ok(new SignInResponse(await sessions.StartForAccountAsync(account, http, ct), null))
            : checkedAccount.Error.ToProblem();
    }

    /// <summary>The visitor's session, the account if signed in, and what it may do among the staff screens.</summary>
    private static async Task<MeResponse> MeAsync(
        ClaimsPrincipal user, IdentityDbContext db, PermissionRegistry permissions, CancellationToken ct)
    {
        var account = user.GetAccountId() is { } accountId
            ? await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId, ct)
            : null;

        return new MeResponse(
            user.GetSessionId(),
            account is null
                ? null
                : new AccountResponse(account.Id, account.FullName, account.Email, account.Role, account.TwoStepEnabled, account.MustChangePassword),
            [.. permissions.All.Where(permission => permission.IsGrantedTo(user)).Select(permission => permission.Key)]);
    }

    private static async ValueTask<object?> RequireClientHeader(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.Request.Headers[ClientHeader] == ClientHeaderValue
            ? await next(context)
            : Error.Forbidden("client_header_missing", $"Auth requests must carry '{ClientHeader}: {ClientHeaderValue}'.").ToProblem();
}
