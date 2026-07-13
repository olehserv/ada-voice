namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>Per-request correlation id, readable by services (audit writer, later tasks)
/// without those services referencing the Api project. The Api's <c>CorrelationContext</c>
/// is the scoped implementation whose value the correlation-id middleware sets.</summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }
}
