using Microsoft.EntityFrameworkCore;

namespace _10xnotes.Data;

/// <summary>
/// Applies pending EF migrations once at startup.
/// </summary>
/// <remarks>
/// This lives in an <see cref="IHostedService"/> rather than inline in <c>Program.cs</c> for
/// two reasons:
/// <list type="bullet">
/// <item><description><c>dotnet ef</c> builds the application host at design time; inline
/// startup code would run migrations against the live database during
/// <c>migrations add</c>.</description></item>
/// <item><description>Failure must not be fatal. A crash-loop would fail the container's
/// HEALTHCHECK and make Coolify de-route the app — the outage recorded in the deployment
/// runbook. Instead the failure is logged and surfaced on <c>/health/ready</c>.</description></item>
/// </list>
/// Coolify runs a single container, so there is no cross-instance migration race.
/// </remarks>
public sealed class DatabaseMigrationHostedService(
    IServiceScopeFactory scopeFactory,
    DatabaseMigrationState state,
    ILogger<DatabaseMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count == 0)
            {
                logger.LogInformation("No pending migrations; database schema is up to date.");
            }
            else
            {
                logger.LogInformation("Applying {Count} pending migration(s): {Migrations}",
                    pending.Count, string.Join(", ", pending));
                await db.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Migrations applied successfully.");
            }

            state.MarkApplied();
        }
        catch (Exception ex)
        {
            // Deliberately not rethrown — see the remarks above.
            state.MarkFailed(ex.Message);
            logger.LogCritical(ex,
                "Database migration failed at startup. The app is running against a schema that " +
                "may be stale; /health/ready will report unhealthy until this is resolved.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
