using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UptimeMonitor.Models;
using UptimeMonitor.Services;

namespace UptimeMonitor.Functions;

/// <summary>On a schedule: check every endpoint at once and record the results.</summary>
public sealed class CheckEndpoints(
    EndpointChecker checker,
    IResultStore store,
    IOptions<MonitorOptions> options,
    ILogger<CheckEndpoints> logger)
{
    // MONITOR_SCHEDULE is an app setting: every 5 minutes in Azure, every
    // minute locally. Six fields: second minute hour day month weekday.
    [Function(nameof(CheckEndpoints))]
    public Task Run([TimerTrigger("%MONITOR_SCHEDULE%")] TimerInfo timer, CancellationToken cancellationToken) =>
        RunChecksAsync(cancellationToken);

    public async Task<IReadOnlyList<CheckResult>> RunChecksAsync(CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(
            options.Value.Endpoints.Select(e => checker.CheckAsync(e, cancellationToken)));

        foreach (var result in results)
        {
            await store.SaveAsync(result, cancellationToken);

            if (result.Success)
            {
                logger.LogInformation("Endpoint up: {Endpoint} HTTP {StatusCode} in {LatencyMs} ms",
                    result.Endpoint, result.StatusCode, result.LatencyMs);
            }
            else
            {
                // Warning, so it reaches Application Insights; the alert rule
                // in infra/main.bicep matches on the "Endpoint down" prefix.
                logger.LogWarning("Endpoint down: {Endpoint} ({Url}): {Error}",
                    result.Endpoint, result.Url, result.Error);
            }
        }

        return results;
    }
}
