using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

namespace OrderFlow.ServiceDefaults;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    /// <summary>Only used for local development when no key is configured.</summary>
    internal const string DevelopmentKey = "orderflow-local-development-signing-key-change-me-0123456789";

    public string Issuer { get; set; } = "orderflow-identity";
    public string Audience { get; set; } = "orderflow";
    public string Key { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 7;

    public static JwtOptions From(IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection(Section).Get<JwtOptions>() ?? new JwtOptions();
        options.Key = configuration["JWT_SECRET"] ?? options.Key;
        if (string.IsNullOrWhiteSpace(options.Key))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("JWT_SECRET (or Jwt:Key) must be configured outside Development.");
            options.Key = DevelopmentKey;
        }
        if (Encoding.UTF8.GetByteCount(options.Key) < 32)
            throw new InvalidOperationException("The JWT signing key must be at least 32 bytes long.");
        return options;
    }

    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(Key));
}

public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// Cross-cutting defaults shared by every service: hosting port, JSON, problem details,
    /// JWT authentication, authorization policies, health checks and OpenAPI.
    /// </summary>
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder)
    {
        // Render (and most PaaS hosts) inject the port to listen on through $PORT.
        var port = Environment.GetEnvironmentVariable("PORT");
        if (!string.IsNullOrWhiteSpace(port))
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        builder.Services.AddProblemDetails(o =>
            o.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Instance = $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
                ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
            });
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        builder.Services.AddOpenApi();
        builder.AddJwtAuthentication();
        return builder;
    }

    public static WebApplicationBuilder AddJwtAuthentication(this WebApplicationBuilder builder)
    {
        var jwt = JwtOptions.From(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(jwt);

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = jwt.SigningKey,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AppClaims.Name,
                    RoleClaimType = AppClaims.Role
                };
                o.Events = new JwtBearerEvents
                {
                    // Browsers cannot send headers on WebSocket upgrades, so SignalR passes the token in the query string.
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(AppPolicies.Authenticated, p => p.RequireAuthenticatedUser())
            .AddPolicy(AppPolicies.Admin, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Admin));

        return builder;
    }

    /// <summary>Standard middleware pipeline and infrastructure endpoints.</summary>
    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();

        // Liveness: process is up (used by the hosting platform; never touches dependencies).
        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = c => c.Tags.Contains("live") })
            .AllowAnonymous();
        // Readiness: database, broker and cache connectivity, rendered as JSON for the admin dashboard.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = c => c.Tags.Contains("ready") || c.Tags.Contains("live"),
            ResponseWriter = WriteHealthJson
        }).AllowAnonymous();

        app.MapOpenApi().AllowAnonymous();
        app.MapScalarApiReference("/docs", o => o.WithTitle($"{app.Environment.ApplicationName} API"))
            .AllowAnonymous();
        app.MapGet("/", () => Results.Redirect("/docs")).ExcludeFromDescription();
        return app;
    }

    private static Task WriteHealthJson(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            durationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1),
                description = e.Value.Description ?? e.Value.Exception?.Message
            })
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonSerializerOptions.Web));
    }
}
