using System.Threading.Channels;

namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>The in-memory hand-off between request-scoped <see cref="AuditWriter"/> callers and
/// the singleton background flush service. A thin seam over <see cref="Channel{T}"/> so
/// <c>AuditWriter</c> and <c>AuditFlushService</c> depend on an interface, not the concrete
/// channel.</summary>
public interface IAuditQueue
{
    /// <summary>Enqueues one entry. Backpressure: if the bounded channel is full, this awaits
    /// until space frees up (see <see cref="AuditBatchingOptions.QueueCapacity"/>) rather than
    /// dropping the entry — an audit row is never silently lost.</summary>
    ValueTask EnqueueAsync(QueuedAuditEntry entry, CancellationToken ct);

    /// <summary>The read side, drained only by <c>AuditFlushService</c>.</summary>
    ChannelReader<QueuedAuditEntry> Reader { get; }
}
