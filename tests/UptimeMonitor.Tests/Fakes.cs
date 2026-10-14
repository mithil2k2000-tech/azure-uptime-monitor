using System.Net;
using UptimeMonitor.Models;
using UptimeMonitor.Services;

namespace UptimeMonitor.Tests;

/// <summary>An HTTP "server" the test decides the answers of.</summary>
public sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public static StubHandler Status(HttpStatusCode code) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(code)));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        respond(request, cancellationToken);
}

public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class InMemoryStore : IResultStore
{
    public List<CheckResult> Saved { get; } = [];

    public Task SaveAsync(CheckResult result, CancellationToken cancellationToken)
    {
        Saved.Add(result);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CheckResult>> GetSinceAsync(string endpoint, DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CheckResult>>(Saved
            .Where(r => r.Endpoint == endpoint && r.CheckedAt >= since)
            .OrderByDescending(r => r.CheckedAt)
            .ToList());
}

public static class Make
{
    public static readonly DateTimeOffset Noon = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    public static MonitoredEndpoint Endpoint(string name = "site", string url = "https://example.com/", int? expected = null, double timeout = 5) =>
        new() { Name = name, Url = url, ExpectedStatus = expected, TimeoutSeconds = timeout };

    public static EndpointChecker Checker(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new FixedTime(Noon));

    public static CheckResult Result(string endpoint, DateTimeOffset at, bool success) =>
        new(endpoint, "https://example.com/", at, success, success ? 200 : 503, 42, success ? null : "HTTP 503");
}
