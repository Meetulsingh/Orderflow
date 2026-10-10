using Microsoft.Extensions.Configuration;
using Npgsql;

namespace OrderFlow.ServiceDefaults;

/// <summary>
/// Resolves infrastructure connection strings from configuration.
/// Cloud providers (Neon, Upstash, CloudAMQP) hand out URL-style connection strings,
/// so both URL and native formats are accepted.
/// </summary>
public static class ConnectionStrings
{
    public static string? Postgres(IConfiguration config)
    {
        var raw = config["DATABASE_URL"] ?? config.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.StartsWith("postgres", StringComparison.OrdinalIgnoreCase) && raw.Contains("://")
            ? FromPostgresUrl(raw)
            : raw;
    }

    public static string? Redis(IConfiguration config)
    {
        var raw = config["REDIS_URL"] ?? config.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.StartsWith("redis", StringComparison.OrdinalIgnoreCase) && raw.Contains("://")
            ? FromRedisUrl(raw)
            : raw;
    }

    public static string? RabbitMq(IConfiguration config)
    {
        var raw = config["RABBITMQ_URL"] ?? config.GetConnectionString("RabbitMQ");
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    internal static string FromPostgresUrl(string url)
    {
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.Trim('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            SslMode = SslMode.Prefer,
            // Neon's free tier suspends compute when idle: allow time for a cold start.
            Timeout = 30,
            CommandTimeout = 60,
            MaxPoolSize = 10
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length != 2) continue;
            var value = Uri.UnescapeDataString(kv[1]);
            switch (kv[0].ToLowerInvariant())
            {
                case "sslmode":
                    builder.SslMode = Enum.Parse<SslMode>(value.Replace("-", ""), ignoreCase: true);
                    break;
                case "channel_binding":
                    builder.ChannelBinding = Enum.Parse<ChannelBinding>(value, ignoreCase: true);
                    break;
            }
        }

        return builder.ConnectionString;
    }

    internal static string FromRedisUrl(string url)
    {
        var uri = new Uri(url);
        var port = uri.Port > 0 ? uri.Port : 6379;
        var parts = new List<string> { $"{uri.Host}:{port}", "abortConnect=false", "connectTimeout=10000" };
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var userInfo = uri.UserInfo.Split(':', 2);
            if (userInfo.Length == 2)
            {
                if (!string.IsNullOrEmpty(userInfo[0]) && userInfo[0] != "default")
                    parts.Add($"user={Uri.UnescapeDataString(userInfo[0])}");
                parts.Add($"password={Uri.UnescapeDataString(userInfo[1])}");
            }
            else
            {
                parts.Add($"password={Uri.UnescapeDataString(userInfo[0])}");
            }
        }
        if (uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase)) parts.Add("ssl=true");
        return string.Join(',', parts);
    }
}
