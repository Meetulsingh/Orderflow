using System.Threading.RateLimiting;
using OrderFlow.Gateway;
using OrderFlow.Identity.API;
using OrderFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Identity is hosted in-process so signing in only has to wake this one service.
builder.AddIdentityModule();

// ---- Reverse proxy (YARP) -------------------------------------------------------------
var services = ProxyConfiguration.LoadServices(builder.Configuration);
var (routes, clusters) = ProxyConfiguration.Build(services);
builder.Services.AddReverseProxy().LoadFromMemory(routes, clusters);

builder.Services.AddHttpClient("health", c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient("warmup", c => c.Timeout = TimeSpan.FromSeconds(120));

// ---- CORS -----------------------------------------------------------------------------
var origins = (builder.Configuration["CORS_ORIGINS"] ?? builder.Configuration["Cors:Origins"] ?? "http://localhost:4200")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(o => o.TrimEnd('/'))
    .ToArray();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (origins.Contains("*")) p.AllowAnyOrigin();
    // Entries like "https://*.onrender.com" allow any subdomain of a trusted host.
    else p.WithOrigins(origins).SetIsOriginAllowedToAllowWildcardSubdomains();
    p.AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("X-Cache").SetPreflightMaxAge(TimeSpan.FromHours(1));
}));

// ---- Rate limiting (per client IP) ----------------------------------------------------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (ctx, ct) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        await Results.Problem(statusCode: 429, title: "Too many requests",
                detail: "You are sending requests too quickly. Please slow down.")
            .ExecuteAsync(ctx.HttpContext);
    };

    o.AddPolicy(ProxyConfiguration.ApiRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 150,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    // Stricter limit on credential endpoints to slow down brute-force attempts.
    o.AddPolicy(ProxyConfiguration.AuthRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 15,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();

app.UseCors();
app.UseRateLimiter();
app.UseServiceDefaults();
app.UseWebSockets();

app.MapSystemEndpoints(services);
app.MapIdentityEndpoints().RequireRateLimiting(ProxyConfiguration.AuthRateLimit);
app.MapReverseProxy();

await app.MigrateIdentityDatabaseAsync();

app.Run();

static string ClientKey(HttpContext http) =>
    http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
