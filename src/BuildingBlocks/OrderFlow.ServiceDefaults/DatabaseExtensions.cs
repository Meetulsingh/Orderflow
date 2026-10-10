using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OrderFlow.ServiceDefaults;

public static class DatabaseExtensions
{
    /// <summary>Used only when no connection string is configured (local dev / design-time tooling).</summary>
    private const string LocalFallback = "Host=localhost;Port=5432;Database=orderflow;Username=postgres;Password=postgres";

    /// <summary>
    /// Registers a PostgreSQL DbContext. Every service owns a dedicated schema
    /// (database-per-service pattern on a shared free-tier Postgres instance).
    /// </summary>
    public static WebApplicationBuilder AddPostgres<TContext>(this WebApplicationBuilder builder, string schema)
        where TContext : DbContext
    {
        var connectionString = ConnectionStrings.Postgres(builder.Configuration);
        if (connectionString is null)
        {
            if (!builder.Environment.IsDevelopment())
                throw new InvalidOperationException("DATABASE_URL (or ConnectionStrings:Postgres) must be configured.");
            connectionString = LocalFallback;
        }

        builder.Services.AddDbContext<TContext>(o => o
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", schema);
                npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
            })
            .UseSnakeCaseNamingConvention());

        builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "postgres", tags: ["ready"],
            timeout: TimeSpan.FromSeconds(10));
        return builder;
    }

    /// <summary>
    /// Applies pending EF Core migrations on startup, retrying while a serverless database wakes up.
    /// </summary>
    public static async Task MigrateDatabaseAsync<TContext>(this WebApplication app, Func<TContext, Task>? seed = null)
        where TContext : DbContext
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Database");
        const int maxAttempts = 8;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TContext>();
                await db.Database.MigrateAsync();
                if (seed is not null) await seed(db);
                logger.LogInformation("Database for {Context} is up to date", typeof(TContext).Name);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                logger.LogWarning(ex, "Database migration attempt {Attempt} failed; retrying in {Delay}s", attempt, delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }
    }
}
