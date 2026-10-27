using Microsoft.Extensions.Diagnostics.HealthChecks;
using NATS.Client.Core;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

public sealed class BrokerReadinessCheck(INatsConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await connection.ConnectAsync().AsTask().WaitAsync(budget.Token);
            await connection.PingAsync(budget.Token);
            return connection.ServerInfo?.AuthRequired == true ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Package execution requires broker authentication.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return HealthCheckResult.Unhealthy("The broker did not answer within the readiness budget."); }
        catch (NatsException) { return HealthCheckResult.Unhealthy("The authenticated broker is unavailable."); }
    }
}
