using System.Text.Json.Serialization;
using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Http.Resilience;
using OrderFlow.Ordering.API.Application;
using OrderFlow.Ordering.API.Application.Behaviors;
using OrderFlow.Ordering.API.Application.Commands;
using OrderFlow.Ordering.API.Application.Queries;
using OrderFlow.Ordering.API.Consumers;
using OrderFlow.Ordering.API.Hubs;
using OrderFlow.Ordering.API.Infrastructure;
using OrderFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddPostgres<OrderingDbContext>(OrderingDbContext.Schema);
builder.AddMessaging("ordering", x =>
{
    x.AddConsumer<PaymentProcessingStartedConsumer>();
    x.AddConsumer<PaymentSucceededConsumer>();
    x.AddConsumer<PaymentFailedConsumer>();
    x.AddConsumer<OrderSubmittedFaultConsumer>();

    // Transactional outbox: messages published inside a request are saved with the order and
    // relayed to RabbitMQ by a background delivery service — no lost or phantom events.
    x.AddEntityFrameworkOutbox<OrderingDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
        o.QueryDelay = TimeSpan.FromSeconds(1);
        o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
    });
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<PlaceOrderCommand>();
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
builder.Services.AddValidatorsFromAssemblyContaining<PlaceOrderCommandValidator>();

builder.Services.AddHttpClient<CatalogClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Catalog"] ?? "http://localhost:5102"))
    .AddStandardResilienceHandler(o =>
    {
        // Free-tier services may be cold-starting; allow generous timeouts.
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
    });

// SignalR has its own serializer settings: keep enums as strings, matching the REST API contract.
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<OrderNotifier>();
builder.Services.AddScoped<CampaignService>();
builder.AddInternalApiKey();

var app = builder.Build();

app.UseServiceDefaults();

var orders = app.MapGroup("/api/orders").WithTags("Orders").RequireAuthorization();

orders.MapPost("/", async (
        PlaceOrderRequest request,
        HttpContext http,
        ISender sender,
        CancellationToken ct) =>
    {
        var user = http.User;
        var idempotencyKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
        var order = await sender.Send(new PlaceOrderCommand(
            user.GetUserId(), user.GetEmail(), user.GetFullName(), idempotencyKey,
            request.Items ?? [], request.ShippingAddress, request.Payment, request.CouponCode), ct);
        return Results.Accepted($"/api/orders/{order.Id}", order);
    })
    .WithSummary("Place an order (202 Accepted — payment is processed asynchronously)");

orders.MapGet("/", (HttpContext http, ISender sender, CancellationToken ct) =>
        sender.Send(new GetMyOrdersQuery(http.User.GetUserId()), ct))
    .WithSummary("Orders of the current user");

orders.MapGet("/{id:guid}", (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        sender.Send(new GetOrderQuery(id, http.User.GetUserId(), http.User.IsAdmin()), ct))
    .WithSummary("Get one order");

var admin = app.MapGroup("/api/admin/orders").WithTags("Admin").RequireAuthorization(AppPolicies.Admin);

admin.MapGet("/", (ISender sender, int? take, CancellationToken ct) => sender.Send(new GetRecentOrdersQuery(take ?? 50), ct))
    .WithSummary("Most recent orders across all customers");

admin.MapGet("/stats", (ISender sender, CancellationToken ct) => sender.Send(new GetOrderStatsQuery(), ct))
    .WithSummary("Order KPIs");

app.MapCampaignEndpoints();
app.MapHub<OrderHub>(OrderHub.Path);

await app.MigrateDatabaseAsync<OrderingDbContext>();

app.Run();

public partial class Program;
