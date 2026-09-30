using System.ComponentModel.DataAnnotations;
using Mrp.Platform.Domain;

namespace Mrp.Platform.Application;

/// <summary>Creates a tenant and its owner user.</summary>
public sealed record SignupRequest(
    [property: Required, StringLength(40, MinimumLength = 3), RegularExpression("^[a-zA-Z0-9][a-zA-Z0-9-]*[a-zA-Z0-9]$")] string CompanyCode,
    [property: Required, StringLength(200, MinimumLength = 2)] string CompanyName,
    [property: Required, EmailAddress, StringLength(200)] string Email,
    [property: Required, StringLength(100, MinimumLength = 8)] string Password,
    [property: Required, StringLength(100, MinimumLength = 1)] string DisplayName,
    TenantPlan Plan = TenantPlan.Starter);

/// <summary>Sign-in request.</summary>
public sealed record LoginRequest(
    [property: Required, StringLength(40, MinimumLength = 3)] string CompanyCode,
    [property: Required, StringLength(200)] string UserName,
    [property: Required, StringLength(100)] string Password,
    [property: StringLength(10)] string? TwoFactorCode = null,
    [property: StringLength(200)] string? Device = null);

/// <summary>Exchange or revoke a refresh token.</summary>
public sealed record RefreshRequest([property: Required, StringLength(200, MinimumLength = 40)] string RefreshToken);

/// <summary>Tokens returned after sign-in or refresh.</summary>
public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn, UserProfile User);

/// <summary>Current user with tenant and permissions.</summary>
public sealed record UserProfile(
    Guid Id,
    string UserName,
    string? Email,
    string DisplayName,
    string Language,
    bool TwoFactorEnabled,
    Guid TenantId,
    string TenantName,
    string TenantSlug,
    TenantPlan Plan,
    string TimeZone,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

/// <summary>Secret for registering an authenticator app.</summary>
public sealed record TwoFactorSetupResponse(string SharedKey, string AuthenticatorUri);

/// <summary>TOTP code from the authenticator app.</summary>
public sealed record TwoFactorCodeRequest([property: Required, StringLength(10, MinimumLength = 6)] string Code);

/// <summary>User list row.</summary>
public sealed record UserDto(Guid Id, string UserName, string? Email, string DisplayName, string Language, bool IsActive, bool TwoFactorEnabled, IReadOnlyList<Guid> RoleIds);

/// <summary>Creates a user.</summary>
public sealed record CreateUserRequest(
    [property: Required, StringLength(100, MinimumLength = 3), RegularExpression("^[a-zA-Z0-9._@-]+$")] string UserName,
    [property: Required, EmailAddress, StringLength(200)] string Email,
    [property: Required, StringLength(100, MinimumLength = 1)] string DisplayName,
    [property: Required, StringLength(100, MinimumLength = 8)] string Password,
    [property: Required, MinLength(1), MaxLength(20)] IReadOnlyList<Guid> RoleIds,
    [property: RegularExpression("^(th|en)$")] string Language = "th");

/// <summary>Updates a user; password is changed only when provided.</summary>
public sealed record UpdateUserRequest(
    [property: Required, EmailAddress, StringLength(200)] string Email,
    [property: Required, StringLength(100, MinimumLength = 1)] string DisplayName,
    [property: Required, MinLength(1), MaxLength(20)] IReadOnlyList<Guid> RoleIds,
    bool IsActive,
    [property: RegularExpression("^(th|en)$")] string Language = "th",
    [property: StringLength(100, MinimumLength = 8)] string? NewPassword = null);

/// <summary>Role with its permissions.</summary>
public sealed record RoleDto(Guid Id, string Name, bool IsSystem, IReadOnlyList<string> Permissions);

/// <summary>Creates or updates a role.</summary>
public sealed record SaveRoleRequest(
    [property: Required, StringLength(100, MinimumLength = 2)] string Name,
    [property: Required, MaxLength(200)] IReadOnlyList<string> Permissions);

/// <summary>Document number format of one document type.</summary>
public sealed record DocumentNumberFormatDto(string DocumentType, string Prefix, NumberingPeriod Period, int Digits, string Separator, string Example);

/// <summary>Updates a document number format.</summary>
public sealed record SaveDocumentNumberFormatRequest(
    [property: Required, StringLength(10, MinimumLength = 1), RegularExpression("^[a-zA-Z0-9]+$")] string Prefix,
    NumberingPeriod Period,
    [property: Range(3, 10)] int Digits,
    [property: RegularExpression("^[-/]?$")] string Separator = "-");

/// <summary>Approval route of a document type.</summary>
public sealed record ApprovalRouteDto(Guid? Id, string DocumentType, string Name, bool IsActive, IReadOnlyList<ApprovalRouteStepDto> Steps);

/// <summary>Step of an approval route.</summary>
public sealed record ApprovalRouteStepDto(
    int StepNo,
    [property: Required, StringLength(100, MinimumLength = 1)] string Name,
    Guid RoleId,
    [property: Range(0, 9999999999999.9999)] decimal? MinAmount,
    bool RequireTwoFactor);

/// <summary>Replaces the route of a document type.</summary>
public sealed record SaveApprovalRouteRequest(
    [property: Required, StringLength(100, MinimumLength = 1)] string Name,
    bool IsActive,
    [property: Required, MaxLength(10)] IReadOnlyList<ApprovalRouteStepDto> Steps);

/// <summary>Approval request shown in the inbox.</summary>
public sealed record ApprovalRequestDto(
    Guid Id,
    string DocumentType,
    Guid DocumentId,
    string DocumentNo,
    string Title,
    decimal? Amount,
    Guid RequestedBy,
    string RequestedByName,
    DateTimeOffset RequestedAt,
    ApprovalStatus Status,
    int CurrentStepNo,
    int TotalSteps,
    string? CurrentStepName,
    bool CanAct,
    IReadOnlyList<ApprovalActionDto> Actions);

/// <summary>Action taken on a request.</summary>
public sealed record ApprovalActionDto(int StepNo, ApprovalActionType Action, Guid ActedBy, string ActedByName, DateTimeOffset ActedAt, string? Comment);

/// <summary>Approve, reject or withdraw with an optional remark.</summary>
public sealed record ApprovalDecisionRequest([property: StringLength(1000)] string? Comment = null);

/// <summary>Setting key and JSON value.</summary>
public sealed record SettingDto(string Key, System.Text.Json.JsonElement Value);
