using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using OrderFlow.Identity.API.Data;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Identity.API.Auth;

public sealed class TokenService(JwtOptions options, TimeProvider clock)
{
    public (string Token, DateTime ExpiresAtUtc) CreateAccessToken(User user)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(options.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim(AppClaims.Subject, user.Id.ToString()),
            new Claim(AppClaims.Email, user.Email),
            new Claim(AppClaims.Name, user.FullName),
            new Claim(AppClaims.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(options.SigningKey, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    /// <summary>Returns the raw refresh token (sent to the client) and its hash (persisted).</summary>
    public (string Raw, string Hash, DateTime ExpiresAtUtc) CreateRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (raw, Hash(raw), clock.GetUtcNow().UtcDateTime.AddDays(options.RefreshTokenDays));
    }

    public static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
