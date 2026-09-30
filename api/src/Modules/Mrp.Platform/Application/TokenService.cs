using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Mrp.Platform.Domain;
using Mrp.SharedKernel.Web;

namespace Mrp.Platform.Application;

/// <summary>Creates access tokens and refresh token values.</summary>
public sealed class TokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    /// <summary>Lifetime of access tokens.</summary>
    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(_options.AccessTokenMinutes);

    /// <summary>Lifetime of refresh tokens.</summary>
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    /// <summary>Hex SHA-256 of a token value; only this is stored.</summary>
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Extracts the tenant id prefix of a refresh token value.</summary>
    public static bool TryReadTenant(string refreshToken, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        var dot = refreshToken.IndexOf('.', StringComparison.Ordinal);
        return dot == 32 && Guid.TryParseExact(refreshToken.AsSpan(0, dot), "N", out tenantId);
    }

    /// <summary>Creates a signed JWT for the user.</summary>
    /// <param name="user">Authenticated user.</param>
    /// <param name="permissions">Permission codes granted through roles.</param>
    /// <param name="mfa">Whether the session passed two-factor authentication.</param>
    /// <param name="now">Current time.</param>
    public string CreateAccessToken(AppUser user, IEnumerable<string> permissions, bool mfa, DateTimeOffset now)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(MrpClaims.TenantId, user.TenantId.ToString()),
            new("name", user.DisplayName),
            new(MrpClaims.AuthMethod, mfa ? "mfa" : "pwd"),
        };
        claims.AddRange(permissions.Distinct(StringComparer.Ordinal).Select(p => new Claim(MrpClaims.Permission, p)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            Expires = now.Add(AccessTokenLifetime).UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>Creates a random refresh token value prefixed with the tenant id.</summary>
    public string CreateRefreshTokenValue(Guid tenantId) =>
        $"{tenantId:N}.{Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48))}";
}
