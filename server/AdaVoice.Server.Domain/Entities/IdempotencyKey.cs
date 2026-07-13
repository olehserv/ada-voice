using AdaVoice.Server.Domain.Abstractions;

namespace AdaVoice.Server.Domain.Entities;

/// <summary>Supports safe request retries. Standalone table, no FKs. No UpdatedAt.
/// See docs/monetize/database-design.md §2 "idempotency_keys".</summary>
public class IdempotencyKey : IHasCreatedAt
{
    public Guid Id { get; set; }

    /// <summary>Client Idempotency-Key header value.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>e.g. "POST /api/invoices"; unique on (key, endpoint).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>SHA-256 of the request body.</summary>
    public string RequestHash { get; set; } = string.Empty;

    /// <summary>Stored HTTP status to replay.</summary>
    public int ResponseStatus { get; set; }

    /// <summary>Raw JSON stored response to replay.</summary>
    public string? ResponseBody { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>created_at + 24h; cleanup job deletes expired rows.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
