using System.Data;
using Dapper;
using FlowHearth.Application.Abstractions.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FlowHearth.Infrastructure.Health;

public sealed class MySqlHealthCheck(IDbConnectionFactory connectionFactory)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!connectionFactory.IsConfigured)
        {
            return HealthCheckResult.Unhealthy(
                "The FlowHearth database connection is not configured.");
        }

        try
        {
            await using var connection =
                await connectionFactory.OpenConnectionAsync(cancellationToken);

            var value = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "SELECT 1;",
                    commandType: CommandType.Text,
                    cancellationToken: cancellationToken));

            return value == 1
                ? HealthCheckResult.Healthy("The FlowHearth database is reachable.")
                : HealthCheckResult.Unhealthy(
                    "The FlowHearth database returned an unexpected readiness result.");
        }
        catch
        {
            return HealthCheckResult.Unhealthy(
                "The FlowHearth database readiness check failed.");
        }
    }
}
