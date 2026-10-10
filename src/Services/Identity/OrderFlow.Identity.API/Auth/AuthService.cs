using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Identity.API.Data;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Identity.API.Auth;

public sealed class AuthService(
    IdentityDbContext db,
    TokenService tokens,
    IPasswordHasher<User> hasher,
    IPublishEndpoint publisher,
    ILogger<AuthService> logger)
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = Normalize(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("An account with this email already exists.");

        var user = new User { Email = email, FullName = request.FullName.Trim(), Role = AppRoles.Customer };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);

        var response = IssueTokens(user);
        await db.SaveChangesAsync(ct);

        try
        {
            await publisher.Publish(new UserRegistered(Guid.NewGuid(), DateTime.UtcNow, user.Id, user.Email, user.FullName), ct);
        }
        catch (Exception ex)
        {
            // Registration must not fail because the welcome-email event could not be published.
            logger.LogWarning(ex, "Failed to publish UserRegistered for {UserId}", user.Id);
        }

        return response;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = Normalize(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
            throw new UnauthorizedException("Invalid email or password.");

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
            throw new UnauthorizedException("Invalid email or password.");
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, request.Password);

        user.LastLoginAtUtc = DateTime.UtcNow;
        var response = IssueTokens(user);
        await db.SaveChangesAsync(ct);
        return response;
    }

    /// <summary>
    /// Rotates the refresh token. Presenting an already-rotated token is treated as theft:
    /// every active token of that user is revoked.
    /// </summary>
    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var hash = TokenService.Hash(request.RefreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new UnauthorizedException("Invalid refresh token.");

        var user = await db.Users.SingleAsync(u => u.Id == token.UserId, ct);

        if (token.RevokedAtUtc is not null)
        {
            logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking all sessions", user.Id);
            await db.RefreshTokens
                .Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow), ct);
            throw new UnauthorizedException("Refresh token has been revoked.");
        }
        if (token.ExpiresAtUtc <= DateTime.UtcNow)
            throw new UnauthorizedException("Refresh token has expired.");

        var response = IssueTokens(user);
        token.RevokedAtUtc = DateTime.UtcNow;
        token.ReplacedByHash = TokenService.Hash(response.RefreshToken);
        await db.SaveChangesAsync(ct);
        return response;
    }

    public async Task LogoutAsync(RefreshRequest request, CancellationToken ct)
    {
        var hash = TokenService.Hash(request.RefreshToken);
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow), ct);
    }

    public async Task<UserDto> GetUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");
        return ToDto(user);
    }

    private AuthResponse IssueTokens(User user)
    {
        var (access, expires) = tokens.CreateAccessToken(user);
        var (raw, hash, refreshExpires) = tokens.CreateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, TokenHash = hash, ExpiresAtUtc = refreshExpires });
        return new AuthResponse(access, expires, raw, ToDto(user));
    }

    private static UserDto ToDto(User u) => new(u.Id, u.Email, u.FullName, u.Role);

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
