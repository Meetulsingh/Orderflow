using Microsoft.EntityFrameworkCore;

namespace OrderFlow.Identity.API.Data;

public sealed class User
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Email { get; set; }
    public required string FullName { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public required string Role { get; set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? LastLoginAtUtc { get; set; }
    public List<RefreshToken> RefreshTokens { get; init; } = [];
}

public sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid UserId { get; init; }
    /// <summary>SHA-256 hash of the opaque token; the raw value is only ever held by the client.</summary>
    public required string TokenHash { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByHash { get; set; }

    public bool IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTime.UtcNow;
}

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string Schema = "identity";

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Email).HasMaxLength(256);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.FullName).HasMaxLength(120);
            e.Property(u => u.Role).HasMaxLength(32);
            e.HasMany(u => u.RefreshTokens).WithOne().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.TokenHash).HasMaxLength(128);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.Property(t => t.ReplacedByHash).HasMaxLength(128);
            e.Ignore(t => t.IsActive);
        });
    }
}
