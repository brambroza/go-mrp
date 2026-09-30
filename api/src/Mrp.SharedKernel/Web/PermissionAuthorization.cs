using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;

namespace Mrp.SharedKernel.Web;

/// <summary>Claim names used in access tokens.</summary>
public static class MrpClaims
{
    /// <summary>Tenant id claim.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>Permission claim; one per granted permission.</summary>
    public const string Permission = "perm";

    /// <summary>Authentication method reference; value <c>mfa</c> when the session passed 2FA.</summary>
    public const string AuthMethod = "amr";

    /// <summary>Wildcard permission held by the tenant owner role.</summary>
    public const string AllPermissions = "*";
}

/// <summary>Requirement that the caller holds a permission code.</summary>
/// <param name="Permission">Permission code such as <c>purchasing.po.approve</c>.</param>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Evaluates <see cref="PermissionRequirement"/> against the token's permission claims.</summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var granted = context.User.FindAll(MrpClaims.Permission)
            .Any(c => c.Value == MrpClaims.AllPermissions || c.Value == requirement.Permission);
        if (granted)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Builds a policy on demand for names of the form <c>perm:&lt;code&gt;</c>.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    /// <summary>Prefix of dynamic permission policies.</summary>
    public const string Prefix = "perm:";

    /// <inheritdoc />
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[Prefix.Length..]))
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}

/// <summary>Endpoint helpers for permission checks.</summary>
public static class PermissionEndpointExtensions
{
    /// <summary>Requires the caller to hold <paramref name="permission"/>.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PermissionPolicyProvider.Prefix + permission);
}
