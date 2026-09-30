using System.ComponentModel.DataAnnotations;

namespace Mrp.Platform.Application;

/// <summary>Token settings bound from configuration section <c>Jwt</c>. The signing key comes from a secret store.</summary>
public sealed class JwtOptions
{
    /// <summary>Configuration section name.</summary>
    public const string Section = "Jwt";

    /// <summary>Token issuer.</summary>
    [Required]
    public string Issuer { get; set; } = "mrp-api";

    /// <summary>Token audience.</summary>
    [Required]
    public string Audience { get; set; } = "mrp-clients";

    /// <summary>HMAC-SHA256 key, at least 32 characters. Never committed; set via environment/secret.</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Access token lifetime in minutes.</summary>
    [Range(1, 120)]
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Refresh token lifetime in days.</summary>
    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 30;
}
