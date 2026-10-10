using System.Security.Claims;

namespace OrderFlow.ServiceDefaults;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(AppClaims.Subject), out var id)
            ? id
            : throw new UnauthorizedException("The access token does not contain a user id.");

    public static string GetEmail(this ClaimsPrincipal user) => user.FindFirstValue(AppClaims.Email) ?? string.Empty;

    public static string GetFullName(this ClaimsPrincipal user) => user.FindFirstValue(AppClaims.Name) ?? string.Empty;

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(AppRoles.Admin);
}

public static class AppClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
}

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Customer = "Customer";
}

public static class AppPolicies
{
    public const string Admin = "admin";
    public const string Authenticated = "authenticated";
}
