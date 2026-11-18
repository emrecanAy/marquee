using System.Security.Claims;
using Marquee.Common.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Marquee.Common.Security;

/// <summary>
/// Something a staff member may do, declared by the module that owns it, with the roles that hold
/// it. Administrators hold every permission. Endpoints ask for permissions, never for roles.
/// </summary>
/// <param name="Key">Stable identifier, e.g. <c>audit.read</c>; the web client uses it to show or hide screens.</param>
public sealed record Permission(string Key, string Description, params string[] Roles)
{
    public bool IsGrantedTo(ClaimsPrincipal user) =>
        user.FindFirstValue(SessionClaims.Role) is { } role
        && (role == Web.Roles.Admin || Roles.Contains(role))
        && !user.HasClaim(claim => claim.Type == SessionClaims.PasswordChangeRequired);
}

/// <summary>Every permission the modules declared, for <c>/api/me</c> to report which ones a user holds.</summary>
public sealed class PermissionRegistry(IEnumerable<PermissionSet> sets)
{
    public IReadOnlyList<Permission> All { get; } = [.. sets.SelectMany(set => set.Permissions)];
}

public sealed record PermissionSet(IReadOnlyList<Permission> Permissions);

internal sealed class PermissionRequirement(Permission permission) : IAuthorizationRequirement
{
    public Permission Permission { get; } = permission;
}

internal sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (requirement.Permission.IsGrantedTo(context.User))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class PermissionExtensions
{
    /// <summary>Closes the endpoint to everyone without the permission, and keeps it as metadata so a test can find endpoints without one.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, Permission permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder
            .RequireAuthorization(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build())
            .WithMetadata(permission);

    /// <summary>Declares a module's permissions and makes sure the handler that checks them is registered.</summary>
    public static IServiceCollection AddPermissions(this IServiceCollection services, params Permission[] permissions)
    {
        services.AddSingleton(new PermissionSet(permissions));
        services.TryAddSingleton<PermissionRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, PermissionHandler>());
        return services;
    }
}
