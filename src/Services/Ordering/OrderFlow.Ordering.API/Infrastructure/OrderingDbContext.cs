using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Ordering.API.Domain;

namespace OrderFlow.Ordering.API.Infrastructure;

public sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : DbContext(options)
{
    public const string Schema = "ordering";

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Coupon> Coupons => Set<Coupon>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Coupon>(e =>
        {
            e.ToTable("coupons");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.Code).HasMaxLength(32);
            e.HasIndex(c => c.Code).IsUnique();
            // One coupon per campaign (e.g. per ISO week): a retried trigger can't create a second one.
            e.Property(c => c.CampaignKey).HasMaxLength(64);
            e.HasIndex(c => c.CampaignKey).IsUnique();
            e.HasIndex(c => c.ExpiresAtUtc);
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.OrderNumber).HasMaxLength(32);
            e.HasIndex(o => o.OrderNumber).IsUnique();
            e.Property(o => o.CustomerEmail).HasMaxLength(256);
            e.Property(o => o.CustomerName).HasMaxLength(120);
            e.Property(o => o.IdempotencyKey).HasMaxLength(64);
            e.HasIndex(o => new { o.UserId, o.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
            e.HasIndex(o => new { o.UserId, o.CreatedAtUtc });
            e.HasIndex(o => o.Status);
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(o => o.PaymentScenario).HasMaxLength(16);
            e.Property(o => o.CardLast4).HasMaxLength(4);
            e.Property(o => o.TransactionReference).HasMaxLength(64);
            e.Property(o => o.FailureReason).HasMaxLength(500);
            e.Property(o => o.Subtotal).HasPrecision(12, 2);
            e.Property(o => o.Discount).HasPrecision(12, 2);
            e.Property(o => o.CouponCode).HasMaxLength(32);
            e.Property(o => o.Total).HasPrecision(12, 2);
            // Optimistic concurrency via PostgreSQL's xmin system column.
            e.Property(o => o.Version).IsRowVersion();

            e.ComplexProperty(o => o.ShippingAddress, a =>
            {
                a.Property(x => x.FullName).HasMaxLength(120);
                a.Property(x => x.Line1).HasMaxLength(200);
                a.Property(x => x.Line2).HasMaxLength(200);
                a.Property(x => x.City).HasMaxLength(100);
                a.Property(x => x.State).HasMaxLength(100);
                a.Property(x => x.PostalCode).HasMaxLength(20);
                a.Property(x => x.Country).HasMaxLength(60);
            });

            e.OwnsMany(o => o.Items, i =>
            {
                i.ToTable("order_items");
                i.WithOwner().HasForeignKey("OrderId");
                i.HasKey(x => x.Id);
                // Keys are generated client-side (UUIDv7); tell EF so new children are INSERTed, not UPDATEd.
                i.Property(x => x.Id).ValueGeneratedNever();
                i.Property(x => x.ProductName).HasMaxLength(120);
                i.Property(x => x.UnitPrice).HasPrecision(12, 2);
                i.Ignore(x => x.LineTotal);
            });
            e.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

            e.OwnsMany(o => o.History, h =>
            {
                h.ToTable("order_status_history");
                h.WithOwner().HasForeignKey("OrderId");
                h.HasKey(x => x.Id);
                h.Property(x => x.Id).ValueGeneratedNever();
                h.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
                h.Property(x => x.Note).HasMaxLength(500);
            });
            e.Navigation(o => o.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        // Transactional outbox tables: events are stored atomically with the order and relayed to RabbitMQ.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
