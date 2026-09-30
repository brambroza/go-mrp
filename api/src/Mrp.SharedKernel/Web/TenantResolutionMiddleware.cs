using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Mrp.SharedKernel.Tenancy;

namespace Mrp.SharedKernel.Web;

/// <summary>
/// Copies tenant and user from the validated access token into the scoped <see cref="ITenantContext"/>.
/// The tenant is never taken from headers, query strings, or request bodies.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    /// <summary>Resolves the tenant for authenticated requests and continues the pipeline.</summary>
    public async Task InvokeAsync(HttpContext context, ITenantContext tenant)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirstValue(MrpClaims.TenantId), out var tenantId)
            && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub"), out var userId))
        {
            tenant.Set(tenantId, userId);
        }

        await next(context);
    }
}
