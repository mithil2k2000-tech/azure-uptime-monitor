namespace UptimeMonitor.Models;

/// <summary>One site to watch. Bound from Monitor__Endpoints__N__* settings.</summary>
public sealed class MonitoredEndpoint
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>The status that counts as up. Null means any 2xx.</summary>
    public int? ExpectedStatus { get; set; }

    public double TimeoutSeconds { get; set; } = 10;
}

public sealed class MonitorOptions
{
    public List<MonitoredEndpoint> Endpoints { get; set; } = [];

    /// <summary>
    /// Everything wrong with the settings, in words. Names become table
    /// partition keys, so they can't contain characters Table storage
    /// forbids there.
    /// </summary>
    public IEnumerable<string> Problems()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (endpoint, i) in Endpoints.Select((e, i) => (e, i)))
        {
            if (string.IsNullOrWhiteSpace(endpoint.Name))
                yield return $"Endpoint {i} has no name.";
            else if (endpoint.Name.IndexOfAny(['/', '\\', '#', '?']) >= 0)
                yield return $"Endpoint name '{endpoint.Name}' contains / \\ # or ?.";
            else if (!seen.Add(endpoint.Name))
                yield return $"Endpoint name '{endpoint.Name}' is used twice.";

            if (!Uri.TryCreate(endpoint.Url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                yield return $"Endpoint '{endpoint.Name}' has an invalid URL: '{endpoint.Url}'.";

            if (endpoint.TimeoutSeconds is <= 0 or > 60)
                yield return $"Endpoint '{endpoint.Name}' timeout must be between 0 and 60 seconds.";
        }
    }
}
