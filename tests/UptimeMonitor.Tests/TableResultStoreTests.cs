using Azure.Data.Tables;
using UptimeMonitor.Services;

namespace UptimeMonitor.Tests;

/// <summary>
/// Against a real Table storage API: Azurite in CI (see .github/workflows/ci.yml).
/// Skipped, by returning early, when AZURITE_CONNECTION_STRING isn't set.
/// </summary>
public class TableResultStoreTests
{
    private static TableClient? Table()
    {
        var connection = Environment.GetEnvironmentVariable("AZURITE_CONNECTION_STRING");
        return string.IsNullOrEmpty(connection)
            ? null
            : new TableClient(connection, "test" + Guid.NewGuid().ToString("N")[..12]);
    }

    [Fact]
    public async Task Saves_and_reads_back_newest_first_within_the_window()
    {
        if (Table() is not { } table) return;
        var store = new TableResultStore(table);

        await store.SaveAsync(Make.Result("site", Make.Noon.AddMinutes(-10), success: true), CancellationToken.None);
        await store.SaveAsync(Make.Result("site", Make.Noon, success: false), CancellationToken.None);
        await store.SaveAsync(Make.Result("site", Make.Noon.AddHours(-30), success: true), CancellationToken.None);
        await store.SaveAsync(Make.Result("other", Make.Noon, success: true), CancellationToken.None);

        var results = await store.GetSinceAsync("site", Make.Noon.AddHours(-24), CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.False(results[0].Success);
        Assert.Equal(Make.Noon, results[0].CheckedAt);
        Assert.Equal(503, results[0].StatusCode);
        Assert.Equal("HTTP 503", results[0].Error);
        Assert.True(results[1].Success);
        Assert.Null(results[1].Error);

        await table.DeleteAsync();
    }
}
