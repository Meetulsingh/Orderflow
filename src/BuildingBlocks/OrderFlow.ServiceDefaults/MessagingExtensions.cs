using System.Security.Authentication;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace OrderFlow.ServiceDefaults;

public static class MessagingExtensions
{
    /// <summary>
    /// Configures MassTransit with retries and service-prefixed queue names. Messages that exhaust
    /// retries are moved to the queue's <c>_error</c> dead-letter queue and a <see cref="Fault{T}"/> is published.
    /// <list type="bullet">
    /// <item>RabbitMQ (CloudAMQP in production) when <c>RABBITMQ_URL</c> is set.</item>
    /// <item>Otherwise, in Development, MassTransit's PostgreSQL transport — a real broker on the local
    /// database, so the whole system runs locally with nothing but Postgres.</item>
    /// </list>
    /// </summary>
    public static WebApplicationBuilder AddMessaging(
        this WebApplicationBuilder builder,
        string servicePrefix,
        Action<IBusRegistrationConfigurator>? configure = null)
    {
        var rabbitUrl = ConnectionStrings.RabbitMq(builder.Configuration);
        if (rabbitUrl is null && !builder.Environment.IsDevelopment())
            throw new InvalidOperationException("RABBITMQ_URL (or ConnectionStrings:RabbitMQ) must be configured.");

        if (rabbitUrl is null) ConfigurePostgresTransportOptions(builder);

        builder.Services.AddMassTransit(x =>
        {
            x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(servicePrefix, includeNamespace: false));
            configure?.Invoke(x);

            if (rabbitUrl is null)
            {
                x.UsingPostgres((context, cfg) =>
                {
                    ConfigureCommon(cfg);
                    cfg.ConfigureEndpoints(context);
                });
                return;
            }

            x.UsingRabbitMq((context, cfg) =>
            {
                var uri = new Uri(rabbitUrl);
                var userInfo = uri.UserInfo.Split(':', 2);
                var secure = uri.Scheme.Equals("amqps", StringComparison.OrdinalIgnoreCase);
                var port = uri.IsDefaultPort || uri.Port <= 0 ? (secure ? 5671 : 5672) : uri.Port;
                var vhost = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
                if (string.IsNullOrEmpty(vhost)) vhost = "/";

                cfg.Host(uri.Host, (ushort)port, vhost, h =>
                {
                    if (userInfo.Length == 2)
                    {
                        h.Username(Uri.UnescapeDataString(userInfo[0]));
                        h.Password(Uri.UnescapeDataString(userInfo[1]));
                    }
                    if (secure) h.UseSsl(s => s.Protocol = SslProtocols.Tls12 | SslProtocols.Tls13);
                    h.Heartbeat(TimeSpan.FromSeconds(30));
                    h.RequestedConnectionTimeout(TimeSpan.FromSeconds(30));
                });

                // Free CloudAMQP plans have low connection/throughput limits; keep prefetch small.
                cfg.PrefetchCount = 8;
                ConfigureCommon(cfg);
                cfg.ConfigureEndpoints(context);
            });
        });

        return builder;
    }

    private static void ConfigurePostgresTransportOptions(WebApplicationBuilder builder)
    {
        var cs = new NpgsqlConnectionStringBuilder(
            ConnectionStrings.Postgres(builder.Configuration)
            ?? "Host=localhost;Port=5432;Database=orderflow;Username=postgres;Password=postgres");

        builder.Services.AddOptions<SqlTransportOptions>().Configure(o =>
        {
            o.Host = cs.Host;
            o.Port = cs.Port;
            o.Database = cs.Database;
            o.Schema = "transport";
            o.Role = "transport";
            o.Username = cs.Username;
            o.Password = cs.Password;
            o.AdminUsername = cs.Username;
            o.AdminPassword = cs.Password;
        });
        builder.Services.AddPostgresMigrationHostedService(create: true, delete: false);
    }

    private static void ConfigureCommon(IBusFactoryConfigurator cfg)
    {
        cfg.UseMessageRetry(r =>
        {
            r.Intervals(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
            r.Ignore<BusinessRuleException>();
        });
    }
}
