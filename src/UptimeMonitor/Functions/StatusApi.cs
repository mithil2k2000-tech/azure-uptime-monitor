using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Options;
using UptimeMonitor.Models;
using UptimeMonitor.Services;

namespace UptimeMonitor.Functions;

/// <summary>GET /api/status: each endpoint's current state and 24-hour uptime.</summary>
public sealed class StatusApi(IResultStore store, IOptions<MonitorOptions> options, TimeProvider time)
{
    [Function("Status")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "status")] HttpRequest request,
        CancellationToken cancellationToken) =>
        new OkObjectResult(await GetStatusAsync(cancellationToken));

    public async Task<IReadOnlyList<EndpointStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow().AddHours(-24);
        var statuses = new List<EndpointStatus>();
        foreach (var endpoint in options.Value.Endpoints)
        {
            var results = await store.GetSinceAsync(endpoint.Name, since, cancellationToken);
            statuses.Add(StatusCalculator.Summarize(endpoint, results));
        }
        return statuses;
    }
}
