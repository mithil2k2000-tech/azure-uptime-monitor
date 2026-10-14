namespace UptimeMonitor.Models;

/// <summary>The outcome of one request to one endpoint.</summary>
public sealed record CheckResult(
    string Endpoint,
    string Url,
    DateTimeOffset CheckedAt,
    bool Success,
    int? StatusCode,
    long LatencyMs,
    string? Error);

/// <summary>What the status API reports for an endpoint.</summary>
public sealed record EndpointStatus(
    string Name,
    string Url,
    string State,
    DateTimeOffset? LastChecked,
    int? LastStatusCode,
    long? LastLatencyMs,
    double? Uptime24hPercent,
    int Checks24h,
    string? LastError);
