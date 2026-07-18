namespace AdaVoice.Server.Api.Infrastructure;

/// <summary>Reads <c>X-Correlation-Id</c> from the request (or mints a new GUID), stores it
/// on the scoped <see cref="CorrelationContext"/> so downstream services (audit writer,
/// <see cref="GlobalExceptionHandler"/>) can read it without touching HTTP concerns, and
/// echoes it back on the response header. Runs first in the pipeline, before headers are
/// sent, so the header can be set directly rather than deferred to
/// <see cref="HttpResponse.OnStarting"/>.</summary>
public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, CorrelationContext correlationContext)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var headerValue)
            && !string.IsNullOrWhiteSpace(headerValue)
            ? headerValue.ToString()
            : Guid.NewGuid().ToString();

        correlationContext.CorrelationId = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        await _next(context);
    }
}
