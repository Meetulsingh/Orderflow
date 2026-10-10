using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Identity.API.Data;

/// <summary>Seeds the demo admin and customer accounts that the login page advertises.</summary>
public static class IdentitySeeder
{
    public static async Task SeedAsync(IdentityDbContext db, IConfiguration config, IPasswordHasher<User> hasher)
    {
        await EnsureUser(db, hasher,
            config["Seed:AdminEmail"] ?? "admin@orderflow.dev",
            config["Seed:AdminPassword"] ?? "Admin@12345",
            "Ada Admin", AppRoles.Admin);

        await EnsureUser(db, hasher,
            config["Seed:DemoEmail"] ?? "demo@orderflow.dev",
            config["Seed:DemoPassword"] ?? "Demo@12345",
            "Dana Demo", AppRoles.Customer);

        await db.SaveChangesAsync();
    }

    private static async Task EnsureUser(IdentityDbContext db, IPasswordHasher<User> hasher,
        string email, string password, string name, string role)
    {
        email = email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email)) return;
        var user = new User { Email = email, FullName = name, Role = role };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
    }
}
