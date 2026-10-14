using UptimeMonitor.Models;

namespace UptimeMonitor.Services;

public static class StatusCalculator
{
    /// <summary>Summarise one endpoint from its results, newest first.</summary>
    public static EndpointStatus Summarize(MonitoredEndpoint endpoint, IReadOnlyList<CheckResult> newestFirst)
    {
        if (newestFirst.Count == 0)
        {
            return new EndpointStatus(endpoint.Name, endpoint.Url, "unknown",
                null, null, null, null, 0, null);
        }

        var last = newestFirst[0];
        var upCount = newestFirst.Count(r => r.Success);
        var uptime = Math.Round(100.0 * upCount / newestFirst.Count, 2);

        return new EndpointStatus(
            endpoint.Name,
            endpoint.Url,
            last.Success ? "up" : "down",
            last.CheckedAt,
            last.StatusCode,
            last.LatencyMs,
            uptime,
            newestFirst.Count,
            last.Error);
    }
}
