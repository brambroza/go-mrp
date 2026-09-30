using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Mrp.Platform.Application;
using Mrp.Platform.Domain;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Web;

namespace Mrp.Platform.Endpoints;

/// <summary>HTTP endpoints of the platform module.</summary>
public static class PlatformEndpoints
{
    /// <summary>Name of the rate limiting policy applied to anonymous auth endpoints.</summary>
    public const string AuthRateLimitPolicy = "auth";

    /// <summary>Maps <c>/auth</c>, <c>/users</c>, <c>/roles</c>, <c>/settings</c> and <c>/approvals</c>.</summary>
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder api)
    {
        MapAuth(api);
        MapUsersAndRoles(api);
        MapSettings(api);
        MapApprovals(api);
        return api;
    }

    private static void MapAuth(IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/signup", (SignupRequest request, AuthService service, CancellationToken ct) => service.SignupAsync(request, ct))
            .Validate<SignupRequest>().AllowAnonymous().RequireRateLimiting(AuthRateLimitPolicy).WithName("Signup");

        auth.MapPost("/login", (LoginRequest request, AuthService service, CancellationToken ct) => service.LoginAsync(request, ct))
            .Validate<LoginRequest>().AllowAnonymous().RequireRateLimiting(AuthRateLimitPolicy).WithName("Login");

        auth.MapPost("/refresh", (RefreshRequest request, AuthService service, CancellationToken ct) => service.RefreshAsync(request, ct))
            .Validate<RefreshRequest>().AllowAnonymous().RequireRateLimiting(AuthRateLimitPolicy).WithName("RefreshToken");

        auth.MapPost("/logout", async (RefreshRequest request, AuthService service, CancellationToken ct) =>
            {
                await service.LogoutAsync(request, ct);
                return Results.NoContent();
            })
            .Validate<RefreshRequest>().AllowAnonymous().WithName("Logout");

        auth.MapGet("/me", (AuthService service, CancellationToken ct) => service.GetProfileAsync(ct))
            .RequireAuthorization().WithName("GetProfile");

        auth.MapPost("/2fa/setup", (AuthService service) => service.SetupTwoFactorAsync())
            .RequireAuthorization().WithName("SetupTwoFactor");

        auth.MapPost("/2fa/enable", async (TwoFactorCodeRequest request, AuthService service) =>
            {
                await service.EnableTwoFactorAsync(request);
                return Results.NoContent();
            })
            .Validate<TwoFactorCodeRequest>().RequireAuthorization().WithName("EnableTwoFactor");

        auth.MapPost("/2fa/disable", async (TwoFactorCodeRequest request, AuthService service, CancellationToken ct) =>
            {
                await service.DisableTwoFactorAsync(request, ct);
                return Results.NoContent();
            })
            .Validate<TwoFactorCodeRequest>().RequireAuthorization().WithName("DisableTwoFactor");
    }

    private static void MapUsersAndRoles(IEndpointRouteBuilder api)
    {
        var users = api.MapGroup("/users").WithTags("Users").RequirePermission(Permissions.UsersManage);
        users.MapGet("/", (string? search, int? page, int? pageSize, UserAdminService service, CancellationToken ct) =>
            service.ListAsync(search, page, pageSize, ct)).WithName("ListUsers");
        users.MapPost("/", (CreateUserRequest request, UserAdminService service, CancellationToken ct) => service.CreateAsync(request, ct))
            .Validate<CreateUserRequest>().WithName("CreateUser");
        users.MapPut("/{id:guid}", (Guid id, UpdateUserRequest request, UserAdminService service, CancellationToken ct) =>
            service.UpdateAsync(id, request, ct)).Validate<UpdateUserRequest>().WithName("UpdateUser");

        var roles = api.MapGroup("/roles").WithTags("Roles");
        roles.MapGet("/", (RoleAdminService service, CancellationToken ct) => service.ListAsync(ct))
            .RequireAuthorization().WithName("ListRoles");
        roles.MapPost("/", (SaveRoleRequest request, RoleAdminService service, CancellationToken ct) => service.CreateAsync(request, ct))
            .Validate<SaveRoleRequest>().RequirePermission(Permissions.RolesManage).WithName("CreateRole");
        roles.MapPut("/{id:guid}", (Guid id, SaveRoleRequest request, RoleAdminService service, CancellationToken ct) =>
            service.UpdateAsync(id, request, ct)).Validate<SaveRoleRequest>().RequirePermission(Permissions.RolesManage).WithName("UpdateRole");

        api.MapGet("/permissions", () => Permissions.ByModule)
            .WithTags("Roles").RequireAuthorization().WithName("ListPermissions");
    }

    private static void MapSettings(IEndpointRouteBuilder api)
    {
        var settings = api.MapGroup("/settings").WithTags("Settings");
        settings.MapGet("/", (TenantSettingsService service, CancellationToken ct) => service.ListAsync(ct))
            .RequireAuthorization().WithName("ListSettings");
        settings.MapPut("/", async (IReadOnlyList<SettingDto> request, TenantSettingsService service, CancellationToken ct) =>
            {
                if (request.Count is 0 or > 50)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["Send 1-50 settings."] });
                }

                await service.SaveAsync(request, ct);
                return Results.Ok(await service.ListAsync(ct));
            })
            .RequirePermission(Permissions.SettingsManage).WithName("SaveSettings");

        var numbering = api.MapGroup("/document-number-formats").WithTags("Settings");
        numbering.MapGet("/", (NumberingAdminService service, CancellationToken ct) => service.ListAsync(ct))
            .RequireAuthorization().WithName("ListDocumentNumberFormats");
        numbering.MapPut("/{documentType}", (string documentType, SaveDocumentNumberFormatRequest request, NumberingAdminService service, CancellationToken ct) =>
            service.SaveAsync(documentType, request, ct))
            .Validate<SaveDocumentNumberFormatRequest>().RequirePermission(Permissions.SettingsManage).WithName("SaveDocumentNumberFormat");

        var routes = api.MapGroup("/approval-routes").WithTags("Approvals");
        routes.MapGet("/", (ApprovalService service, CancellationToken ct) => service.ListRoutesAsync(ct))
            .RequireAuthorization().WithName("ListApprovalRoutes");
        routes.MapPut("/{documentType}", (string documentType, SaveApprovalRouteRequest request, ApprovalService service, CancellationToken ct) =>
            service.SaveRouteAsync(documentType, request, ct))
            .Validate<SaveApprovalRouteRequest>().RequirePermission(Permissions.ApprovalsConfigure).WithName("SaveApprovalRoute");
    }

    private static void MapApprovals(IEndpointRouteBuilder api)
    {
        var approvals = api.MapGroup("/approvals").WithTags("Approvals").RequireAuthorization();
        approvals.MapGet("/inbox", (int? page, int? pageSize, ApprovalService service, CancellationToken ct) =>
            service.InboxAsync(page, pageSize, ct)).WithName("ApprovalInbox");
        approvals.MapGet("/mine", (int? page, int? pageSize, ApprovalService service, CancellationToken ct) =>
            service.MineAsync(page, pageSize, ct)).WithName("MyApprovalRequests");
        approvals.MapGet("/{id:guid}", (Guid id, ApprovalService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("GetApprovalRequest");
        approvals.MapGet("/documents/{documentType}/{documentId:guid}", (string documentType, Guid documentId, ApprovalService service, CancellationToken ct) =>
            service.ForDocumentAsync(documentType, documentId, ct)).WithName("GetDocumentApprovals");

        approvals.MapPost("/{id:guid}/approve", (Guid id, ApprovalDecisionRequest request, ClaimsPrincipal user, ApprovalService service, CancellationToken ct) =>
            service.ApproveAsync(id, request.Comment, PassedTwoFactor(user), ct))
            .Validate<ApprovalDecisionRequest>().WithName("ApproveRequest");
        approvals.MapPost("/{id:guid}/reject", (Guid id, ApprovalDecisionRequest request, ClaimsPrincipal user, ApprovalService service, CancellationToken ct) =>
            service.RejectAsync(id, request.Comment, PassedTwoFactor(user), ct))
            .Validate<ApprovalDecisionRequest>().WithName("RejectRequest");
        approvals.MapPost("/{id:guid}/withdraw", (Guid id, ApprovalDecisionRequest request, ApprovalService service, CancellationToken ct) =>
            service.WithdrawAsync(id, request.Comment, ct))
            .Validate<ApprovalDecisionRequest>().WithName("WithdrawRequest");
    }

    private static bool PassedTwoFactor(ClaimsPrincipal user) =>
        user.FindAll(MrpClaims.AuthMethod).Any(c => c.Value == "mfa");
}
