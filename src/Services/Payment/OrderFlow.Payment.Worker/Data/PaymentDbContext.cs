using Microsoft.EntityFrameworkCore;

namespace OrderFlow.Payment.Worker.Data;

public enum PaymentStatus { Succeeded, Declined }

public sealed class PaymentRecord
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid OrderId { get; init; }
    public required string OrderNumber { get; init; }
    public Guid UserId { get; init; }
    public required string CustomerEmail { get; init; }
    public decimal Amount { get; init; }
    public required string CardLast4 { get; init; }
    public PaymentStatus Status { get; init; }
    public string? TransactionReference { get; init; }
    public string? FailureReason { get; init; }
    public DateTime ProcessedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public const string Schema = "payment";

    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<PaymentRecord>(e =>
        {
            e.ToTable("payments");
            e.HasKey(p => p.Id);
            // One payment per order: the unique index is the idempotency guarantee.
            e.HasIndex(p => p.OrderId).IsUnique();
            e.Property(p => p.OrderNumber).HasMaxLength(32);
            e.Property(p => p.CustomerEmail).HasMaxLength(256);
            e.Property(p => p.CardLast4).HasMaxLength(4);
            e.Property(p => p.Amount).HasPrecision(12, 2);
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(p => p.TransactionReference).HasMaxLength(64);
            e.Property(p => p.FailureReason).HasMaxLength(300);
        });
    }
}
