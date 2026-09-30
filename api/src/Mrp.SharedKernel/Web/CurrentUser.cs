using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Mrp.SharedKernel.Domain;

namespace Mrp.SharedKernel.Web;

/// <summary>Permissions of the caller, for checks that depend on request data (e.g. document type).</summary>
public interface ICurrentUser
{
    /// <summary>Whether the caller holds the permission.</summary>
    bool Has(string permission);

    /// <summary>Throws a 403 rule violation when the caller lacks the permission.</summary>
    void Require(string permission);
}

/// <summary>Reads permissions from the access token of the current HTTP request.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    /// <inheritdoc />
    public bool Has(string permission)
    {
        var user = accessor.HttpContext?.User;
        return user?.Identity?.IsAuthenticated == true
            && user.FindAll(MrpClaims.Permission).Any(c => c.Value == MrpClaims.AllPermissions || c.Value == permission);
    }

    /// <inheritdoc />
    public void Require(string permission)
    {
        if (!Has(permission))
        {
            throw DomainException.Forbidden("common.forbidden", $"Permission '{permission}' is required.");
        }
    }
}
