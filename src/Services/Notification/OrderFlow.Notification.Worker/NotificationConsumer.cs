using System.Globalization;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Notification.Worker.Data;
using OrderFlow.Notification.Worker.Email;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Notification.Worker;

/// <summary>
/// Subscribes to every business event: writes the audit/event log, sends (simulated) customer emails,
/// and records dead-letter alerts when another service faults on a message.
/// One consumer type → one queue bound to all exchanges, which keeps the free broker's queue count low.
/// </summary>
public sealed class NotificationConsumer(
    NotificationDbContext db,
    IEmailSender sender,
    EmailOptions emailOptions,
    ILogger<NotificationConsumer> logger) :
    IConsumer<UserRegistered>,
    IConsumer<OrderSubmitted>,
    IConsumer<PaymentProcessingStarted>,
    IConsumer<PaymentSucceeded>,
    IConsumer<PaymentFailed>,
    IConsumer<ProductStockLow>,
    IConsumer<Fault<OrderSubmitted>>
{
    // Services run with InvariantGlobalization, so format currency explicitly rather than via a culture.
    private static string Usd(decimal amount) => "$" + amount.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>The welcome email is really delivered (through Brevo when configured), not only recorded.</summary>
    public async Task Consume(ConsumeContext<UserRegistered> context)
    {
        var m = context.Message;
        // A redelivered event must not send a second welcome email.
        if (await db.EventLogs.AsNoTracking().AnyAsync(e => e.EventId == m.EventId, context.CancellationToken)) return;

        var welcome = WelcomeEmail.Build(m.Email, m.FullName, emailOptions.FrontendUrl);
        string delivery;
        if (!EmailAddresses.IsDeliverable(m.Email))
        {
            delivery = "not sent: demo/test address";
        }
        else
        {
            // Transient provider errors throw, so MassTransit retries the whole message before anything is recorded.
            var result = await sender.SendAsync(welcome, context.CancellationToken);
            delivery = result.Delivered ? result.Status : $"rejected: {result.Error}";
        }

        await Record(context, m.EventId, "identity", null, EventSeverity.Info,
            $"New customer registered: {m.Email} (welcome email {delivery})",
            Email(m.UserId, m.Email, welcome.Subject, welcome.Text));
    }

    public Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        var m = context.Message;
        var lines = string.Join("\n", m.Items.Select(i => $"  • {i.Quantity} × {i.ProductName} — {Usd(i.UnitPrice * i.Quantity)}"));
        return Record(context, m.EventId, "ordering", m.OrderNumber, EventSeverity.Info,
            $"Order {m.OrderNumber} submitted ({Usd(m.Total)}, {m.Items.Count} line(s))",
            Email(m.UserId, m.CustomerEmail, $"We received your order {m.OrderNumber}",
                $"Hi {m.CustomerName},\n\nThanks for your order! We're processing your payment now.\n\n{lines}\n\nTotal: {Usd(m.Total)}"));
    }

    public Task Consume(ConsumeContext<PaymentProcessingStarted> context)
    {
        var m = context.Message;
        return Record(context, m.EventId, "payment", m.OrderNumber, EventSeverity.Info,
            $"Payment authorisation started for {m.OrderNumber}");
    }

    public Task Consume(ConsumeContext<PaymentSucceeded> context)
    {
        var m = context.Message;
        return Record(context, m.EventId, "payment", m.OrderNumber, EventSeverity.Success,
            $"Payment captured for {m.OrderNumber}: {Usd(m.Amount)} ({m.TransactionReference})",
            Email(m.UserId, m.CustomerEmail, $"Order {m.OrderNumber} confirmed ✅",
                $"Great news — your payment of {Usd(m.Amount)} was approved (ref {m.TransactionReference}). Your order is confirmed and being prepared for shipment."));
    }

    public Task Consume(ConsumeContext<PaymentFailed> context)
    {
        var m = context.Message;
        return Record(context, m.EventId, "payment", m.OrderNumber, EventSeverity.Warning,
            $"Payment failed for {m.OrderNumber}: {m.Reason}",
            Email(m.UserId, m.CustomerEmail, $"Payment issue with order {m.OrderNumber}",
                $"Unfortunately we couldn't process the payment for order {m.OrderNumber}: {m.Reason}\nNo money has been taken. Please try again with a different card."));
    }

    public Task Consume(ConsumeContext<ProductStockLow> context)
    {
        var m = context.Message;
        return Record(context, m.EventId, "catalog", null, EventSeverity.Warning,
            $"Low stock: \"{m.ProductName}\" has {m.RemainingStock} unit(s) left");
    }

    public async Task Consume(ConsumeContext<Fault<OrderSubmitted>> context)
    {
        var fault = context.Message;
        var exception = fault.Exceptions.FirstOrDefault();
        var order = fault.Message;
        var service = fault.FaultingService();

        if (!await db.DeadLetters.AnyAsync(d => d.Id == fault.FaultId, context.CancellationToken))
        {
            db.DeadLetters.Add(new DeadLetterEntry
            {
                Id = fault.FaultId,
                MessageType = nameof(OrderSubmitted),
                OrderNumber = order.OrderNumber,
                ExceptionType = exception?.ExceptionType ?? "Unknown",
                ExceptionMessage = exception?.Message ?? "Unknown error",
                Host = $"{service}@{fault.Host?.MachineName}",
                FaultedAtUtc = fault.Timestamp
            });
        }

        // Only a payment failure affects the customer; other services' faults are operational alerts.
        var email = service == "payment"
            ? Email(order.UserId, order.CustomerEmail, $"Order {order.OrderNumber} could not be completed",
                "Our payment provider is experiencing an outage and your order could not be completed. You have not been charged.")
            : null;

        await Record(context, fault.FaultId, service, order.OrderNumber, EventSeverity.Error,
            $"DEAD-LETTERED: {service} failed to process OrderSubmitted for {order.OrderNumber} after all retries — {exception?.Message}",
            email);
    }

    private async Task Record<T>(ConsumeContext<T> context, Guid eventId, string source, string? orderNumber,
        EventSeverity severity, string summary, NotificationMessage? email = null) where T : class
    {
        var ct = context.CancellationToken;
        if (await db.EventLogs.AnyAsync(e => e.EventId == eventId, ct))
        {
            logger.LogInformation("Duplicate event {EventId} ignored", eventId);
            return;
        }

        db.EventLogs.Add(new EventLogEntry
        {
            EventId = eventId,
            EventType = EventTypeName(typeof(T)),
            Source = source,
            OrderNumber = orderNumber,
            Summary = summary,
            Severity = severity,
            Payload = JsonSerializer.Serialize(context.Message, JsonSerializerOptions.Web),
            OccurredAtUtc = context.SentTime ?? DateTime.UtcNow
        });

        if (email is not null)
        {
            db.Notifications.Add(email);
            // In production this would hand off to an email provider (SendGrid, SES, SMTP).
            logger.LogInformation("📧 Email to {Recipient}: {Subject}", email.Recipient, email.Subject);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (!await db.EventLogs.AsNoTracking().AnyAsync(e => e.EventId == eventId, ct)) throw;
            logger.LogInformation("Concurrent duplicate event {EventId} ignored", eventId);
        }
    }

    private static NotificationMessage Email(Guid userId, string to, string subject, string body) =>
        new() { UserId = userId, Recipient = to, Subject = subject, Body = body };

    private static string EventTypeName(Type type) =>
        type.IsGenericType ? $"{type.Name[..type.Name.IndexOf('`')]}<{type.GetGenericArguments()[0].Name}>" : type.Name;
}
