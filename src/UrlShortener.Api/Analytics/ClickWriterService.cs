using Microsoft.Data.SqlClient;
using UrlShortener.Api.Data;

namespace UrlShortener.Api.Analytics;

/// <summary>
/// Inserts queued clicks in batches (ADR-0004). Waits on the channel, never on a timer, so an idle app opens no
/// database connection and Azure SQL can auto-pause.
/// </summary>
public sealed partial class ClickWriterService(
    ClickQueue queue, IServiceScopeFactory scopeFactory, ILogger<ClickWriterService> logger) : BackgroundService
{
    // Bounds what one failed batch loses and keeps each insert short.
    public const int MaxBatchSize = 100;

    // Cancelled only when StopAsync's graceful-shutdown timeout runs out. ExecuteAsync's stoppingToken cannot be used:
    // BackgroundService.StopAsync cancels it before waiting, which would abandon the clicks still queued.
    private readonly CancellationTokenSource _shutdownTimeout = new();
    private long _failedBatches;
    private long _droppedLogged;

    /// <summary>Batches whose insert failed and were discarded (not retried: a poisoned item would loop).</summary>
    public long FailedBatches => Interlocked.Read(ref _failedBatches);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var token = _shutdownTimeout.Token;
        var batch = new List<Click>(MaxBatchSize);
        try
        {
            // Ends normally when StopAsync completes the queue and everything queued has been written.
            while (await queue.Reader.WaitToReadAsync(token))
            {
                while (batch.Count < MaxBatchSize && queue.Reader.TryRead(out var e))
                {
                    batch.Add(new Click
                    {
                        LinkId = e.LinkId,
                        OccurredAt = e.OccurredAt,
                        ReferrerHost = e.ReferrerHost,
                        UserAgent = e.UserAgent,
                    });
                }

                await InsertAsync(batch, token);
                batch.Clear();
                LogNewDrops();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutdown timed out: what is still queued is lost (at-most-once delivery, ADR-0004).
        }
        finally
        {
            LogNewDrops(); // drops after the last batch (e.g. TryWrite after Complete() during shutdown) are still reported
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        await using var registration = cancellationToken.Register(_shutdownTimeout.Cancel);
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _shutdownTimeout.Dispose();
        base.Dispose();
    }

    private async Task InsertAsync(List<Click> batch, CancellationToken cancellationToken)
    {
        try
        {
            // A hosted service is a singleton; the DbContext is scoped, so each batch gets its own scope.
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LinksDbContext>();
            db.Clicks.AddRange(batch);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Never rethrow: an exception escaping ExecuteAsync stops the host (CLAUDE.md §9). Type and SQL error number
            // only; the message and the entities can contain referrer hosts and user agents.
            Interlocked.Increment(ref _failedBatches);
            if (logger.IsEnabled(LogLevel.Warning))
            {
                var count = batch.Count;
                var exceptionType = ex.GetType().Name;
                var sqlNumber = (ex.InnerException as SqlException)?.Number;
                LogBatchFailed(logger, count, exceptionType, sqlNumber);
            }
        }
    }

    private void LogNewDrops()
    {
        var dropped = queue.Dropped;
        if (dropped > _droppedLogged)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                var delta = dropped - _droppedLogged;
                LogDropped(logger, delta);
            }

            _droppedLogged = dropped;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Discarded a batch of {Count} clicks: {ExceptionType} (SQL error {SqlNumber})")]
    private static partial void LogBatchFailed(ILogger logger, int count, string exceptionType, int? sqlNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} clicks dropped since the last report (queue full or stopped)")]
    private static partial void LogDropped(ILogger logger, long count);
}
