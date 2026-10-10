using FluentValidation;
using OrderFlow.Catalog.API.Consumers;
using OrderFlow.Catalog.API.Data;
using OrderFlow.Catalog.API.Products;
using OrderFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddPostgres<CatalogDbContext>(CatalogDbContext.Schema);
builder.AddMessaging("catalog", x => x.AddConsumer<DeductStockOnPaymentSucceeded>());

// Distributed cache: Upstash Redis in production, in-process memory cache locally.
var redis = ConnectionStrings.Redis(builder.Configuration);
if (redis is not null)
{
    builder.Services.AddStackExchangeRedisCache(o =>
    {
        o.Configuration = redis;
        o.InstanceName = "orderflow:";
    });
    builder.Services.AddHealthChecks().AddRedis(redis, name: "redis", tags: ["ready"], timeout: TimeSpan.FromSeconds(5));
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddScoped<ProductCache>();
builder.Services.AddValidatorsFromAssemblyContaining<UpsertProductRequestValidator>();

var app = builder.Build();

app.UseServiceDefaults();
app.MapProductEndpoints();

await app.MigrateDatabaseAsync<CatalogDbContext>(CatalogSeeder.SeedAsync);

app.Run();
