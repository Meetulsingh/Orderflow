using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OrderFlow.ServiceDefaults;

/// <summary>
/// Shared secret for service-to-service and scheduler calls (the weekly campaign trigger, the marketing
/// recipient list). Set INTERNAL_API_KEY explicitly, or it is derived from the JWT signing key that every
/// service already has, so no extra secret has to be distributed.
/// </summary>
public sealed class InternalApiKey
{
    public const string Header = "X-Internal-Key";

    public string Value { get; }

    private InternalApiKey(string value) => Value = value;

    public static InternalApiKey From(IConfiguration configuration, IHostEnvironment environment)
    {
        var explicitKey = configuration["INTERNAL_API_KEY"];
        if (!string.IsNullOrWhiteSpace(explicitKey)) return new InternalApiKey(explicitKey.Trim());

        var jwt = JwtOptions.From(configuration, environment);
        var derived = HMACSHA256.HashData(Encoding.UTF8.GetBytes(jwt.Key), "orderflow-internal-api"u8.ToArray());
        return new InternalApiKey(Convert.ToBase64String(derived).Replace('+', '-').Replace('/', '_').TrimEnd('='));
    }

    public bool Matches(string? presented) =>
        presented is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(Value));
}

public static class InternalApiExtensions
{
    public static WebApplicationBuilder AddInternalApiKey(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(InternalApiKey.From(builder.Configuration, builder.Environment));
        return builder;
    }

    /// <summary>Only callers presenting the internal key (other services, the scheduler) may call this endpoint.</summary>
    public static TBuilder RequireInternalKey<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var key = context.HttpContext.RequestServices.GetRequiredService<InternalApiKey>();
            return key.Matches(context.HttpContext.Request.Headers[InternalApiKey.Header].FirstOrDefault())
                ? await next(context)
                : Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized",
                    detail: "A valid internal API key is required.");
        });
}
