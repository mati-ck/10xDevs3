using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace _10xnotes.Data;

/// <summary>
/// Surfaces the outcome of the startup migration on <c>/health/ready</c>. Tagged "ready" so it
/// can never affect the liveness probe the container health check uses.
/// </summary>
public sealed class DatabaseMigrationHealthCheck(DatabaseMigrationState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (state.Applied)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Migrations applied."));
        }

        var message = state.FailureMessage is null
            ? "Startup migration has not completed yet."
            : $"Startup migration failed: {state.FailureMessage}";

        return Task.FromResult(HealthCheckResult.Unhealthy(message));
    }
}
