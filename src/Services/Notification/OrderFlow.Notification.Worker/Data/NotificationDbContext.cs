using Microsoft.EntityFrameworkCore;

namespace OrderFlow.Notification.Worker.Data;

public enum EventSeverity { Info, Success, Warning, Error }

/// <summary>Audit trail of every integration event observed on the bus (powers the admin event monitor).</summary>
public sealed class EventLogEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid EventId { get; init; }
    public required string EventType { get; init; }
    public required string Source { get; init; }
    public string? OrderNumber { get; init; }
    public required string Summary { get; init; }
    public EventSeverity Severity { get; init; }
    public required string Payload { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public DateTime RecordedAtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>A simulated outbound email.</summary>
public sealed class NotificationMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid? UserId { get; init; }
    public required string Recipient { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public string Channel { get; init; } = "email";
    public DateTime SentAtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>A message that exhausted its retries and was moved to an error (dead-letter) queue.</summary>
public sealed class DeadLetterEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string MessageType { get; init; }
    public string? OrderNumber { get; init; }
    public required string ExceptionType { get; init; }
    public required string ExceptionMessage { get; init; }
    public string? Host { get; init; }
    public DateTime FaultedAtUtc { get; init; }
}

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public const string Schema = "notification";

    public DbSet<EventLogEntry> EventLogs => Set<EventLogEntry>();
    public DbSet<NotificationMessage> Notifications => Set<NotificationMessage>();
    public DbSet<DeadLetterEntry> DeadLetters => Set<DeadLetterEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<EventLogEntry>(e =>
        {
            e.ToTable("event_logs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.EventId).IsUnique();
            e.HasIndex(x => x.OccurredAtUtc);
            e.Property(x => x.EventType).HasMaxLength(100);
            e.Property(x => x.Source).HasMaxLength(50);
            e.Property(x => x.OrderNumber).HasMaxLength(32);
            e.Property(x => x.Summary).HasMaxLength(500);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Payload).HasColumnType("jsonb");
        });

        modelBuilder.Entity<NotificationMessage>(e =>
        {
            e.ToTable("notifications");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.SentAtUtc });
            e.Property(x => x.Recipient).HasMaxLength(256);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.Body).HasMaxLength(4000);
            e.Property(x => x.Channel).HasMaxLength(16);
        });

        modelBuilder.Entity<DeadLetterEntry>(e =>
        {
            e.ToTable("dead_letters");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.FaultedAtUtc);
            e.Property(x => x.MessageType).HasMaxLength(100);
            e.Property(x => x.OrderNumber).HasMaxLength(32);
            e.Property(x => x.ExceptionType).HasMaxLength(200);
            e.Property(x => x.ExceptionMessage).HasMaxLength(2000);
            e.Property(x => x.Host).HasMaxLength(200);
        });
    }
}
