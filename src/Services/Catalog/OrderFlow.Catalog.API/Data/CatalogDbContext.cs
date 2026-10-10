using Microsoft.EntityFrameworkCore;

namespace OrderFlow.Catalog.API.Data;

public sealed class Product
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Category { get; set; }
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string? ImageUrl { get; set; }
    public double Rating { get; set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Idempotency record: an order's stock is only deducted once even if the event is redelivered.</summary>
public sealed class ProcessedOrder
{
    public Guid OrderId { get; init; }
    public DateTime ProcessedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";

    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProcessedOrder> ProcessedOrders => Set<ProcessedOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(120);
            e.Property(p => p.Description).HasMaxLength(1000);
            e.Property(p => p.Category).HasMaxLength(60);
            e.Property(p => p.Price).HasPrecision(12, 2);
            e.Property(p => p.ImageUrl).HasMaxLength(500);
            e.HasIndex(p => p.Category);
            e.HasIndex(p => p.Name);
        });

        modelBuilder.Entity<ProcessedOrder>(e => e.HasKey(p => p.OrderId));
    }
}
