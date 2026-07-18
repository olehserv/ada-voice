using System.Threading.Channels;

namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>Bounded <see cref="Channel{T}"/>-backed <see cref="IAuditQueue"/>. Bounded +
/// <see cref="BoundedChannelFullMode.Wait"/> so a full queue applies backpressure to the
/// enqueueing request instead of dropping an audit row (see <see cref="IAuditQueue"/>).
/// Singleton: the channel is the single hand-off between every request-scoped
/// <see cref="AuditWriter"/> and the one background <c>AuditFlushService</c>.</summary>
public sealed class AuditQueue : IAuditQueue
{
    private readonly Channel<QueuedAuditEntry> _channel;

    public AuditQueue(AuditBatchingOptions options)
    {
        _channel = Channel.CreateBounded<QueuedAuditEntry>(
            new BoundedChannelOptions(options.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
            });
    }

    public ValueTask EnqueueAsync(QueuedAuditEntry entry, CancellationToken ct) =>
        _channel.Writer.WriteAsync(entry, ct);

    public ChannelReader<QueuedAuditEntry> Reader => _channel.Reader;
}
