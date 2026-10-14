using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using UptimeMonitor.Models;

namespace UptimeMonitor.Services;

public interface IResultStore
{
    Task SaveAsync(CheckResult result, CancellationToken cancellationToken);

    /// <summary>Results for one endpoint since a moment, newest first.</summary>
    Task<IReadOnlyList<CheckResult>> GetSinceAsync(string endpoint, DateTimeOffset since, CancellationToken cancellationToken);
}

/// <summary>
/// Row keys count down, so Table storage's ascending sort returns the
/// newest result first and "since" becomes a simple range query.
/// </summary>
public static class RowKeys
{
    public static string For(DateTimeOffset moment) =>
        (DateTimeOffset.MaxValue.UtcTicks - moment.UtcTicks).ToString("D19");
}

/// <summary>Results in Azure Table storage: one partition per endpoint.</summary>
public sealed class TableResultStore(TableClient table) : IResultStore
{
    private int _tableReady;

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _tableReady) == 1) return;
        await table.CreateIfNotExistsAsync(cancellationToken);
        Volatile.Write(ref _tableReady, 1);
    }

    public async Task SaveAsync(CheckResult result, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);
        var entity = new TableEntity(result.Endpoint, RowKeys.For(result.CheckedAt))
        {
            ["Url"] = result.Url,
            ["CheckedAt"] = result.CheckedAt,
            ["Success"] = result.Success,
            ["LatencyMs"] = result.LatencyMs,
        };
        if (result.StatusCode is int code) entity["StatusCode"] = code;
        if (result.Error is not null) entity["Error"] = result.Error;

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<IReadOnlyList<CheckResult>> GetSinceAsync(
        string endpoint, DateTimeOffset since, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);
        var cutoff = RowKeys.For(since);
        // Parameters, not string concatenation: the SDK escapes them.
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {endpoint} and RowKey le {cutoff}");

        var results = new List<CheckResult>();
        await foreach (var e in table.QueryAsync<TableEntity>(filter, cancellationToken: cancellationToken))
        {
            results.Add(new CheckResult(
                e.PartitionKey,
                e.GetString("Url") ?? "",
                e.GetDateTimeOffset("CheckedAt") ?? e.Timestamp ?? default,
                e.GetBoolean("Success") ?? false,
                e.GetInt32("StatusCode"),
                e.GetInt64("LatencyMs") ?? 0,
                e.GetString("Error")));
        }
        return results;
    }
}

public static class TableClientFactory
{
    /// <summary>
    /// Locally: a connection string (Azurite). In Azure: the table endpoint
    /// and the function's managed identity, so no storage key exists anywhere.
    /// </summary>
    public static TableClient Create(IConfiguration configuration)
    {
        var tableName = configuration["Monitor:TableName"] ?? "uptimeresults";

        var connectionString = configuration["Storage:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
            return new TableClient(connectionString, tableName);

        var endpoint = configuration["Storage:TableEndpoint"]
            ?? throw new InvalidOperationException(
                "Set Storage__ConnectionString (local) or Storage__TableEndpoint (Azure).");
        return new TableServiceClient(new Uri(endpoint), new DefaultAzureCredential())
            .GetTableClient(tableName);
    }
}
