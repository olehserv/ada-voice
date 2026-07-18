using AdaVoice.Server.Domain.Entities;
using AdaVoice.Server.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>Periodically drains <see cref="IAuditQueue"/> and batch-persists the rows to
/// <c>audit_logs</c>. Runs on its own timer, in its own DI scope per tick — never the scope of
/// the request that enqueued the row (that scope is long gone by flush time). A fresh scope is
/// safe here because <c>AuditLog</c> is not tenant-owned (no query-filter/tenant-stamping
/// concern) and this service only ever inserts.
///
/// A failed flush retains its batch in <see cref="_retryBuffer"/> and retries on the next tick
/// (or the final shutdown flush) rather than losing the rows outright — the queue itself has
/// already been drained of them, so this is the only place they are held pending a retry.
/// Only one flush ever runs at a time (the periodic loop, then — after it has fully stopped —
/// one bounded shutdown flush), so <see cref="_retryBuffer"/> needs no locking.</summary>
public sealed class AuditFlushService : BackgroundService
{
    private readonly IAuditQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AuditBatchingOptions _options;
    private readonly ILogger<AuditFlushService> _logger;
    private readonly List<QueuedAuditEntry> _retryBuffer = [];

    public AuditFlushService(
        IAuditQueue queue,
        IServiceScopeFactory scopeFactory,
        AuditBatchingOptions options,
        ILogger<AuditFlushService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.FlushIntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FlushAsync(allowRetry: true, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown: stoppingToken was cancelled while awaiting the next tick.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Let the periodic loop above observe cancellation and fully stop FIRST, so the final
        // drain below never runs concurrently with it (no locking needed on _retryBuffer/queue).
        await base.StopAsync(cancellationToken);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            // Bounded, best-effort, no retry loop: there is no "next tick" once the host is
            // stopping, so a failure here is logged and the rows are accepted as lost rather
            // than risking a hang at shutdown.
            await FlushAsync(allowRetry: false, timeoutCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Final audit flush on shutdown failed; queued rows were not persisted.");
        }
    }

    private async Task FlushAsync(bool allowRetry, CancellationToken ct)
    {
        var batch = new List<QueuedAuditEntry>(_retryBuffer);
        _retryBuffer.Clear();

        while (batch.Count < _options.MaxBatchSize && _queue.Reader.TryRead(out var entry))
        {
            batch.Add(entry);
        }

        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AdaVoiceDbContext>();
            db.AuditLogs.AddRange(batch.Select(ToAuditLog));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (allowRetry)
        {
            _retryBuffer.AddRange(batch);
            _logger.LogError(ex, "Audit batch save failed ({Count} rows); will retry on the next flush.", batch.Count);
        }
    }

    private static AuditLog ToAuditLog(QueuedAuditEntry queued) => new()
    {
        TenantId = queued.Entry.TenantId,
        ActorUserId = queued.Entry.ActorUserId,
        ActorType = queued.Entry.ActorType,
        Action = queued.Entry.Action,
        EntityType = queued.Entry.EntityType,
        EntityId = queued.Entry.EntityId,
        Ip = queued.Entry.Ip,
        CorrelationId = queued.CorrelationId,
        Data = queued.Entry.DataJson,
        CreatedAt = queued.CreatedAt,
    };
}
