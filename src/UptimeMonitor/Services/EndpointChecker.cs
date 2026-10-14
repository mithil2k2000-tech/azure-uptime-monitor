using System.Net.Http;
using UptimeMonitor.Models;

namespace UptimeMonitor.Services;

/// <summary>
/// Makes one request and says whether the endpoint is up. Never throws for
/// a down endpoint: a refused connection, a timeout and a 500 are all just
/// results. Only the caller cancelling stops it.
/// </summary>
public sealed class EndpointChecker(HttpClient http, TimeProvider time)
{
    public async Task<CheckResult> CheckAsync(MonitoredEndpoint endpoint, CancellationToken cancellationToken)
    {
        var checkedAt = time.GetUtcNow();
        var started = time.GetTimestamp();
        long Elapsed() => (long)time.GetElapsedTime(started).TotalMilliseconds;

        CheckResult Down(string error, int? status = null) =>
            new(endpoint.Name, endpoint.Url, checkedAt, false, status, Elapsed(), error);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(endpoint.TimeoutSeconds));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Url);
            // Headers are enough to judge the status; don't download the page.
            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            var code = (int)response.StatusCode;
            var up = endpoint.ExpectedStatus is int expected
                ? code == expected
                : response.IsSuccessStatusCode;

            return up
                ? new CheckResult(endpoint.Name, endpoint.Url, checkedAt, true, code, Elapsed(), null)
                : Down($"HTTP {code}", code);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Down($"timed out after {endpoint.TimeoutSeconds:0.#}s");
        }
        catch (HttpRequestException ex)
        {
            return Down(ex.Message);
        }
    }
}
