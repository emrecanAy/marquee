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

/// <summary>
/// The cinema's staff accounts. Administrators only. Any change to someone's access signs that person
/// out everywhere at once, and there is always at least one active administrator.
/// </summary>
internal static class StaffEndpoints
{
    public static readonly Permission ManageStaff = new("staff.manage", "Personel hesaplarını yönetir");

    public sealed record MemberResponse(
        Guid Id, string FullName, string Email, string Role, bool IsActive, bool TwoStepEnabled, bool MustChangePassword,
        DateTimeOffset? LastSignInAt)
    {
        public static MemberResponse From(Account account) => new(
            account.Id, account.FullName, account.Email, account.Role, account.IsActive, account.TwoStepEnabled,
            account.MustChangePassword, account.LastSignInAt);
    }

    public sealed record AddMemberRequest(string? FullName, string? Email, string? Role);

    public sealed record UpdateMemberRequest(string? FullName, string? Role, bool? IsActive);

    /// <summary>Shown once, to be passed on; it only lets the person choose their own password.</summary>
    public sealed record TemporaryPassword(MemberResponse Member, string Password);

    public static void Map(IEndpointRouteBuilder api)
    {
        var staff = api.MapGroup("/staff").RequirePermission(ManageStaff).WithTags("Staff");
        staff.MapGet("", async (IdentityDbContext db, CancellationToken ct) =>
            (await db.Accounts.AsNoTracking()
                .Where(account => Roles.Staff.Contains(account.Role))
                .OrderByDescending(account => account.IsActive).ThenBy(account => account.FullName)
                .ToListAsync(ct))
            .ConvertAll(MemberResponse.From));
        staff.MapPost("", AddAsync);
        staff.MapPut("/{memberId:guid}", UpdateAsync);
        staff.MapPost("/{memberId:guid}/password-reset", ResetPasswordAsync);
    }

    private static async Task<IResult> AddAsync(
        AddMemberRequest request, IdentityDbContext db, IPasswordHasher<Account> hasher, TimeProvider time, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || request.Email?.Contains('@') != true)
        {
            return Error.Validation("invalid_member", "A name and a valid e-mail address are required.").ToProblem();
        }

        if (request.Role is null || !Roles.Staff.Contains(request.Role))
        {
            return Error.Validation("invalid_role", "The role is programmer, auditor or admin.").ToProblem();
        }

        var member = Account.Create(request.Email, request.FullName, request.Role, time.GetUtcNow());
        var password = Secrets.NewTemporaryPassword();
        member.SetTemporaryPassword(hasher.HashPassword(member, password));
        db.Accounts.Add(member);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return Error.Conflict("email_in_use", "Another account already uses this e-mail address.").ToProblem();
        }

        return Results.Created($"/api/staff/{member.Id}", new TemporaryPassword(MemberResponse.From(member), password));
    }

    private static async Task<IResult> UpdateAsync(
        Guid memberId, UpdateMemberRequest request, ClaimsPrincipal admin, IdentityDbContext db, Sessions sessions,
        CancellationToken ct)
    {
        if (memberId == admin.GetAccountId() && (request.Role is not null || request.IsActive is not null))
        {
            return Error.Conflict("cannot_change_self", "Ask another administrator to change your own role or access.").ToProblem();
        }

        // Every change to staff access takes this lock: two administrators demoting each other at the
        // same moment would otherwise each see the other still in charge, and nobody would be left.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended('identity:staff', 0))", ct);

        var member = await db.Accounts.SingleOrDefaultAsync(account => account.Id == memberId, ct);
        if (member is null || !member.IsStaff)
        {
            // Customer accounts are not managed here; to this endpoint they do not exist.
            return MemberNotFound().ToProblem();
        }

        var versionBefore = member.SessionVersion;
        if (!string.IsNullOrWhiteSpace(request.FullName))
        {
            member.Rename(request.FullName);
        }

        if (request.Role is not null && member.ChangeStaffRole(request.Role) is { IsSuccess: false } invalid)
        {
            return invalid.Error.ToProblem();
        }

        if (request.IsActive is { } isActive)
        {
            member.SetActive(isActive);
        }

        var adminsLeft = await db.Accounts.CountAsync(
            account => account.Id != member.Id && account.IsActive && account.Role == Roles.Admin, ct);
        if (adminsLeft == 0 && !(member.IsActive && member.Role == Roles.Admin))
        {
            return Error.Conflict("last_admin", "There must be at least one active administrator.").ToProblem();
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        if (member.SessionVersion != versionBefore)
        {
            await sessions.RevokeAllAsync(member.Id, ct);
        }

        return Results.Ok(MemberResponse.From(member));
    }

    private static async Task<IResult> ResetPasswordAsync(
        Guid memberId, ClaimsPrincipal admin, IdentityDbContext db, IPasswordHasher<Account> hasher, Sessions sessions,
        CancellationToken ct)
    {
        var member = await db.Accounts.SingleOrDefaultAsync(account => account.Id == memberId, ct);
        if (member is null || !member.IsStaff)
        {
            return MemberNotFound().ToProblem();
        }

        if (member.Id == admin.GetAccountId())
        {
            return Error.Conflict("cannot_change_self", "Change your own password from your account page.").ToProblem();
        }

        var password = Secrets.NewTemporaryPassword();
        member.SetTemporaryPassword(hasher.HashPassword(member, password));
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllAsync(member.Id, ct);
        return Results.Ok(new TemporaryPassword(MemberResponse.From(member), password));
    }

    private static Error MemberNotFound() => Error.NotFound("member_not_found", "There is no staff member with this id.");
}
