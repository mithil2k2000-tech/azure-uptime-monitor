using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using UptimeMonitor.Functions;
using UptimeMonitor.Models;
using UptimeMonitor.Services;

namespace UptimeMonitor.Tests;

public class StatusCalculatorTests
{
    [Fact]
    public void No_results_means_unknown()
    {
        var status = StatusCalculator.Summarize(Make.Endpoint(), []);

        Assert.Equal("unknown", status.State);
        Assert.Null(status.Uptime24hPercent);
        Assert.Equal(0, status.Checks24h);
    }

    [Fact]
    public void State_comes_from_the_newest_result_and_uptime_from_all()
    {
        var newestFirst = new[]
        {
            Make.Result("site", Make.Noon, success: false),
            Make.Result("site", Make.Noon.AddMinutes(-5), success: true),
            Make.Result("site", Make.Noon.AddMinutes(-10), success: true),
            Make.Result("site", Make.Noon.AddMinutes(-15), success: true),
        };

        var status = StatusCalculator.Summarize(Make.Endpoint(), newestFirst);

        Assert.Equal("down", status.State);
        Assert.Equal(75.0, status.Uptime24hPercent);
        Assert.Equal(4, status.Checks24h);
        Assert.Equal("HTTP 503", status.LastError);
    }
}

public class RowKeyTests
{
    [Fact]
    public void Newer_results_sort_first()
    {
        var older = RowKeys.For(Make.Noon);
        var newer = RowKeys.For(Make.Noon.AddSeconds(1));

        Assert.True(string.CompareOrdinal(newer, older) < 0);
        Assert.Equal(19, newer.Length);
    }
}

public class MonitorOptionsTests
{
    [Fact]
    public void Valid_settings_have_no_problems()
    {
        var options = new MonitorOptions { Endpoints = [Make.Endpoint("a"), Make.Endpoint("b")] };
        Assert.Empty(options.Problems());
    }

    [Fact]
    public void Each_bad_setting_is_named()
    {
        var options = new MonitorOptions
        {
            Endpoints =
            [
                Make.Endpoint("dup"),
                Make.Endpoint("dup"),
                Make.Endpoint("bad/name"),
                Make.Endpoint("ftp", url: "ftp://example.com"),
                Make.Endpoint("slow", timeout: 120),
            ],
        };

        var problems = options.Problems().ToList();

        Assert.Equal(4, problems.Count);
        Assert.Contains(problems, p => p.Contains("used twice"));
        Assert.Contains(problems, p => p.Contains("bad/name"));
        Assert.Contains(problems, p => p.Contains("invalid URL"));
        Assert.Contains(problems, p => p.Contains("timeout"));
    }
}

public class FunctionTests
{
    private static IOptions<MonitorOptions> Options(params MonitoredEndpoint[] endpoints) =>
        Microsoft.Extensions.Options.Options.Create(new MonitorOptions { Endpoints = [.. endpoints] });

    [Fact]
    public async Task Every_endpoint_is_checked_and_every_result_stored()
    {
        var handler = new StubHandler((request, _) => Task.FromResult(new HttpResponseMessage(
            request.RequestUri!.Host == "up.example" ? HttpStatusCode.OK : HttpStatusCode.BadGateway)));
        var store = new InMemoryStore();
        var function = new CheckEndpoints(Make.Checker(handler), store,
            Options(Make.Endpoint("up", "https://up.example/"), Make.Endpoint("down", "https://down.example/")),
            NullLogger<CheckEndpoints>.Instance);

        var results = await function.RunChecksAsync(CancellationToken.None);

        Assert.Equal(2, store.Saved.Count);
        Assert.True(results.Single(r => r.Endpoint == "up").Success);
        Assert.Equal("HTTP 502", results.Single(r => r.Endpoint == "down").Error);
    }

    [Fact]
    public async Task Status_reports_each_configured_endpoint_over_24_hours()
    {
        var store = new InMemoryStore();
        store.Saved.Add(Make.Result("site", Make.Noon.AddMinutes(-5), success: true));
        store.Saved.Add(Make.Result("site", Make.Noon.AddMinutes(-10), success: false));
        store.Saved.Add(Make.Result("site", Make.Noon.AddHours(-30), success: false)); // too old to count

        var api = new StatusApi(store, Options(Make.Endpoint("site"), Make.Endpoint("never-checked")),
            new FixedTime(Make.Noon));

        var statuses = await api.GetStatusAsync(CancellationToken.None);

        var site = statuses.Single(s => s.Name == "site");
        Assert.Equal("up", site.State);
        Assert.Equal(50.0, site.Uptime24hPercent);
        Assert.Equal(2, site.Checks24h);
        Assert.Equal("unknown", statuses.Single(s => s.Name == "never-checked").State);
    }
}
