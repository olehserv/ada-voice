namespace AdaVoice.Server.Domain.Abstractions;

/// <summary>Marks an entity that records both create and update times. Append-only tables
/// (audit_logs, idempotency_keys) implement <see cref="IHasCreatedAt"/> only, so the
/// interceptor never touches an UpdatedAt column they do not have.</summary>
public interface IHasTimestamps : IHasCreatedAt
{
    DateTimeOffset UpdatedAt { get; set; }
}
