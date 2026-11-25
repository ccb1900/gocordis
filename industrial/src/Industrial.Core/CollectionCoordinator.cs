using System.Diagnostics;
using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Down-time backfill planner. It does not rely on LastSuccessDate;
/// the ledger decides which files are already delivered.</summary>
public sealed class HistoricalRangePlanner : IHistoricalRangePlanner
{
    /// <summary>Offline backfill scans the last <paramref name="exclusiveLookback"/>
    /// window, not "everything before the cutoff". The ledger (never an
    /// in-memory last-success date) decides what is already delivered.</summary>
    public SourceDateRange Plan(DateTimeOffset now, TimeSpan exclusiveLookback) =>
        new(now - exclusiveLookback, now);
}

/// <summary>Bounded retry with exponential backoff. No infinite loops; the
/// ledger record owns the attempt count across process restarts.</summary>
public static class BoundedRetry
{
    public static async ValueTask<T> RunAsync<T>(
        int attempts,
        Func<int, CancellationToken, ValueTask<T>> operation,
        CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(attempt, ct);
            }
            catch when (attempt < attempts && !ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20 * (1 << (attempt - 1))), ct);
            }
        }
    }
}

/// <summary>One collection execution. Transforms are supplied by plugins as
/// delegates so new nodes never require changing this coordinator.</summary>
public static class IndustrialTelemetry
{
    public static readonly ActivitySource Source = new("Gocordis.Industrial.Collection");
}

public sealed class CollectionCoordinator : ICollectionCoordinator
{
    private readonly IResourceRegistry _resources;
    private readonly IFileSource _source;
    private readonly ICollectionLedger _ledger;
    private readonly IRecordReader? _reader;

    public CollectionCoordinator(
        IResourceRegistry resources, IFileSource source, ICollectionLedger ledger,
        IRecordReader? reader = null)
    {
        _resources = resources;
        _source = source;
        _ledger = ledger;
        _reader = reader;
    }

    public async ValueTask<string> RunAsync(
        string workflowId,
        string resourceId,
        SourceDateRange range,
        IRecordReader reader,
        IReadOnlyList<IRecordSink> sinks,
        IArchiveSink? archive,
        CancellationToken ct = default)
    {
        using var activity = IndustrialTelemetry.Source.StartActivity("collection.run");
        activity?.SetTag("workflow.id", workflowId);
        activity?.SetTag("resource.id", resourceId);
        if (await _resources.GetAsync(resourceId, ct) is null)
        {
            throw new InvalidOperationException($"resource '{resourceId}' not found");
        }

        var run = await _ledger.StartAsync(workflowId, ct);
        try
        {
            var files = await _source.DiscoverAsync(resourceId, range, ct);
            var anySuccess = false;
            var anyFailure = false;
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!await _source.IsStableAsync(file, TimeSpan.FromMilliseconds(200), ct))
                {
                    continue;
                }

                // File status is a roll-up, never a skip decision. A file that
                // already succeeded for target A must still be delivered to a
                // newly configured target B; only target-level delivery rows
                // decide whether work is outstanding.
                var known = await _ledger.GetFileByIdentityAsync(file.ResourceId, file.Identity.Key, ct);
                var ledgerFile = known ?? await _ledger.DiscoverAsync(run.Id, file, ct);
                var fileSucceeded = false;
                var fileFailed = false;
                foreach (var sink in sinks)
                {
                    var key = $"{file.Identity.Key}->{sink.TargetId}";
                    var delivery = await _ledger.StartDeliveryAsync(
                        ledgerFile.Id, sink.TargetId, key, "record", ct);
                    if (delivery.Status == "Succeeded")
                    {
                        fileSucceeded = true;
                        continue;
                    }

                    if (!await _ledger.TryClaimDeliveryAsync(delivery.Id, ct))
                    {
                        continue;
                    }

                    try
                    {
                        var result = await BoundedRetry.RunAsync(
                            3,
                            async (_, token) => await sink.WriteAsync(
                                ledgerFile.Id, reader.ReadAsync(file, token), key, token),
                            ct);
                        await _ledger.CompleteDeliveryAsync(delivery.Id, result.Records, ct);
                        fileSucceeded = true;
                    }
                    catch (Exception ex)
                    {
                        fileFailed = true;
                        await _ledger.FailDeliveryAsync(delivery.Id, ex.Message, ct);
                    }
                }

                if (archive is not null)
                {
                    var key = $"{file.Identity.Key}->{archive.TargetId}";
                    var delivery = await _ledger.StartDeliveryAsync(
                        ledgerFile.Id, archive.TargetId, key, "archive", ct);
                    if (delivery.Status == "Succeeded")
                    {
                        fileSucceeded = true;
                    }
                    else if (await _ledger.TryClaimDeliveryAsync(delivery.Id, ct))
                    {
                        try
                        {
                            await BoundedRetry.RunAsync(
                                3,
                                async (_, token) =>
                                {
                                    await archive.ArchiveAsync(file, key, token);
                                    return true;
                                },
                                ct);
                            await _ledger.CompleteDeliveryAsync(delivery.Id, 1, ct);
                            fileSucceeded = true;
                        }
                        catch (Exception ex)
                        {
                            fileFailed = true;
                            await _ledger.FailDeliveryAsync(delivery.Id, ex.Message, ct);
                        }
                    }
                }

                await _ledger.MarkFileAsync(
                    ledgerFile.Id, fileFailed ? "PartialFailure" : "Succeeded", ct);
                anySuccess |= fileSucceeded;
                anyFailure |= fileFailed;
            }

            var status = anyFailure ? anySuccess ? "PartialFailure" : "Failed" : "Succeeded";
            await _ledger.CompleteRunAsync(run.Id, status, null, ct);
            return run.Id;
        }
        catch (Exception ex)
        {
            await _ledger.CompleteRunAsync(run.Id, "Failed", ex.Message, ct);
            throw;
        }
    }

    public ValueTask<int> RetryFailedAsync(
        IReadOnlyList<IRecordSink> sinks, IArchiveSink? archive, CancellationToken ct = default) =>
        RetryFailedAsync(null, sinks, archive, ct);

    public async ValueTask<int> RetryFailedAsync(
        string? targetId,
        IReadOnlyList<IRecordSink> sinks,
        IArchiveSink? archive,
        CancellationToken ct = default)
    {
        var retried = 0;
        foreach (var delivery in await _ledger.GetRetriableDeliveriesAsync(targetId, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (!await _ledger.TryClaimDeliveryAsync(delivery.Id, ct))
            {
                continue;
            }

            var file = await _ledger.GetFileAsync(delivery.CollectionFileId, ct);
            if (file is null)
            {
                await _ledger.FailDeliveryAsync(delivery.Id, "ledger file no longer exists", ct);
                continue;
            }

            var discovered = await LocateAsync(file, ct);
            if (discovered is null)
            {
                await _ledger.FailDeliveryAsync(delivery.Id, "source file no longer exists", ct);
                continue;
            }

            try
            {
                if (delivery.TargetKind == "archive" ||
                    (archive is not null &&
                     string.Equals(delivery.TargetId, archive.TargetId, StringComparison.Ordinal)))
                {
                    if (archive is null)
                    {
                        throw new InvalidOperationException(
                            $"no archive capability is registered for target '{delivery.TargetId}'");
                    }

                    await BoundedRetry.RunAsync(
                        3,
                        async (_, token) =>
                        {
                            await archive.ArchiveAsync(discovered, delivery.IdempotencyKey, token);
                            return true;
                        },
                        ct);
                    await _ledger.CompleteDeliveryAsync(delivery.Id, 1, ct);
                }
                else
                {
                    var sink = sinks.FirstOrDefault(candidate =>
                        string.Equals(candidate.TargetId, delivery.TargetId, StringComparison.Ordinal));
                    if (sink is null || _reader is null)
                    {
                        throw new InvalidOperationException(
                            $"no reader/sink capability is registered for target '{delivery.TargetId}'");
                    }

                    var result = await BoundedRetry.RunAsync(
                        3,
                        async (_, token) => await sink.WriteAsync(
                            file.Id, _reader.ReadAsync(discovered, token),
                            delivery.IdempotencyKey, token),
                        ct);
                    await _ledger.CompleteDeliveryAsync(delivery.Id, result.Records, ct);
                }

                retried++;
            }
            catch (Exception ex)
            {
                await _ledger.FailDeliveryAsync(delivery.Id, ex.Message, ct);
            }
        }

        return retried;
    }

    private async ValueTask<DiscoveredFile?> LocateAsync(CollectionFile file, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(file.Identity.SourceResourceId))
        {
            var resource = await _resources.GetAsync(file.Identity.SourceResourceId, ct);
            if (resource?.Configuration.TryGetValue("root", out var configuredRoot) == true &&
                File.Exists(Path.Combine(configuredRoot?.ToString() ?? string.Empty,
                    file.Identity.RelativePath)))
            {
                var root = configuredRoot!.ToString()!;
                var fullPath = Path.Combine(root, file.Identity.RelativePath);
                var candidate = FileIdentityFactory.Create(fullPath, root, resource.Id, file.SourceDate);
                if (candidate.Key == file.Identity.Key)
                {
                    return new DiscoveredFile(candidate, fullPath, file.SourceDate, resource.Id);
                }
            }
        }

        foreach (var resource in await _resources.ListAsync(ct))
        {
            if (!resource.Configuration.TryGetValue("root", out var value))
            {
                continue;
            }

            var root = value?.ToString() ?? string.Empty;
            var fullPath = Path.Combine(root, file.Identity.RelativePath);
            if (File.Exists(fullPath))
            {
                var candidate = FileIdentityFactory.Create(fullPath, root, resource.Id, file.SourceDate);
                if (candidate.RelativePath == file.Identity.RelativePath &&
                    candidate.ContentHash == file.Identity.ContentHash &&
                    candidate.Length == file.Identity.Length)
                {
                    return new DiscoveredFile(candidate, fullPath, file.SourceDate, resource.Id);
                }
            }
        }

        return null;
    }
}
