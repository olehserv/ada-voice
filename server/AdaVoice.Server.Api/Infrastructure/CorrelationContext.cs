using AdaVoice.Server.Infrastructure.Auth;

namespace AdaVoice.Server.Api.Infrastructure;

/// <summary>Scoped, mutable holder for the current request's correlation id. Registered as
/// both itself and <see cref="ICorrelationContext"/> so the middleware (which needs to set
/// the value) and downstream services (which only need to read it) share one instance per
/// request. If nothing has set it yet, reading it lazily mints a GUID so callers never see
/// an empty id.</summary>
public sealed class CorrelationContext : ICorrelationContext
{
    private string? _correlationId;

    public string CorrelationId
    {
        get => _correlationId ??= Guid.NewGuid().ToString();
        set => _correlationId = value;
    }
}
