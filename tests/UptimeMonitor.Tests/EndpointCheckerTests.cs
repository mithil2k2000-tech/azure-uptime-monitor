using System.Net;

namespace UptimeMonitor.Tests;

public class EndpointCheckerTests
{
    [Fact]
    public async Task A_2xx_response_is_up()
    {
        var result = await Make.Checker(StubHandler.Status(HttpStatusCode.OK))
            .CheckAsync(Make.Endpoint(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.Null(result.Error);
        Assert.Equal(Make.Noon, result.CheckedAt);
    }

    [Fact]
    public async Task A_server_error_is_down_with_the_status()
    {
        var result = await Make.Checker(StubHandler.Status(HttpStatusCode.ServiceUnavailable))
            .CheckAsync(Make.Endpoint(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal("HTTP 503", result.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.OK, false)]
    public async Task An_expected_status_overrides_the_2xx_rule(HttpStatusCode actual, bool up)
    {
        var result = await Make.Checker(StubHandler.Status(actual))
            .CheckAsync(Make.Endpoint(expected: 404), CancellationToken.None);

        Assert.Equal(up, result.Success);
    }

    [Fact]
    public async Task A_refused_connection_is_down_not_an_exception()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("Connection refused"));

        var result = await Make.Checker(handler).CheckAsync(Make.Endpoint(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.StatusCode);
        Assert.Contains("Connection refused", result.Error);
    }

    [Fact]
    public async Task A_slow_endpoint_times_out()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await Make.Checker(handler).CheckAsync(Make.Endpoint(timeout: 0.2), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("timed out after 0.2s", result.Error);
    }

    [Fact]
    public async Task The_caller_cancelling_is_not_reported_as_down()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Make.Checker(StubHandler.Status(HttpStatusCode.OK)).CheckAsync(Make.Endpoint(), cancelled.Token));
    }
}
