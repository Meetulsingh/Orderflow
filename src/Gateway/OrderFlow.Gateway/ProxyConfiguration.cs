using System.Net;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Gateway;

public sealed record DownstreamService(string Name, string Address);

public static class ProxyConfiguration
{
    public const string ApiRateLimit = "api";
    public const string AuthRateLimit = "auth";

    public static IReadOnlyList<DownstreamService> LoadServices(IConfiguration config) =>
    [
        new("identity", Resolve(config, "Identity", "http://localhost:5101")),
        new("catalog", Resolve(config, "Catalog", "http://localhost:5102")),
        new("ordering", Resolve(config, "Ordering", "http://localhost:5103")),
        new("payment", Resolve(config, "Payment", "http://localhost:5104")),
        new("notification", Resolve(config, "Notification", "http://localhost:5105"))
    ];

    private static string Resolve(IConfiguration config, string name, string fallback)
    {
        var value = config[$"Services:{name}"];
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        // Allow bare hostnames (e.g. "orderflow-catalog.onrender.com") as well as full URLs.
        return value.Contains("://") ? value.TrimEnd('/') : $"https://{value.TrimEnd('/')}";
    }

    public static (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) Build(IReadOnlyList<DownstreamService> services)
    {
        var routes = new List<RouteConfig>
        {
            Route("auth", "identity", "/api/auth/{**rest}", rateLimit: AuthRateLimit),
            Route("products", "catalog", "/api/products/{**rest}"),
            Route("orders", "ordering", "/api/orders/{**rest}", AppPolicies.Authenticated),
            Route("admin-orders", "ordering", "/api/admin/orders/{**rest}", AppPolicies.Admin),
            Route("order-hub", "ordering", "/hubs/{**rest}", AppPolicies.Authenticated, rateLimit: null),
            Route("admin-payments", "payment", "/api/admin/payments/{**rest}", AppPolicies.Admin),
            Route("admin-events", "notification", "/api/admin/events/{**rest}", AppPolicies.Admin),
            Route("admin-dead-letters", "notification", "/api/admin/dead-letters/{**rest}", AppPolicies.Admin),
            Route("admin-notifications", "notification", "/api/admin/notifications/{**rest}", AppPolicies.Admin),
            Route("my-notifications", "notification", "/api/notifications/{**rest}", AppPolicies.Authenticated)
        };

        var clusters = services.Select(s => new ClusterConfig
        {
            ClusterId = s.Name,
            Destinations = new Dictionary<string, DestinationConfig> { ["primary"] = new() { Address = s.Address } },
            HttpRequest = new ForwarderRequestConfig
            {
                // Free-tier services can take ~1 minute to cold start.
                ActivityTimeout = TimeSpan.FromSeconds(120),
                // HTTP/1.1 keeps WebSocket upgrades (SignalR) simple across TLS-terminating hosts.
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            }
        }).ToList();

        return (routes, clusters);
    }

    private static RouteConfig Route(string id, string cluster, string path, string? policy = null, string? rateLimit = ApiRateLimit) => new()
    {
        RouteId = id,
        ClusterId = cluster,
        Match = new RouteMatch { Path = path },
        AuthorizationPolicy = policy,
        RateLimiterPolicy = rateLimit
    };
}
