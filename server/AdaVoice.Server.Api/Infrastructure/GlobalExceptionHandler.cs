using AdaVoice.Server.Infrastructure.Auth;
using Microsoft.AspNetCore.Diagnostics;

namespace AdaVoice.Server.Api.Infrastructure;

/// <summary>Last-resort handler for unhandled exceptions. Logs the full exception server-side
/// but writes only a generic <c>application/problem+json</c> body to the client — no
/// exception message, type, or stack trace ever crosses the response boundary. The
/// correlation id lets support match a client-reported error to the server log entry that
/// has the real detail.
///
/// <see cref="IExceptionHandler"/> is a singleton, so the request-scoped
/// <see cref="ICorrelationContext"/> is resolved from the request's service provider at
/// handling time rather than captured in the constructor (which would be a captive
/// dependency and fails DI scope validation).</summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var correlationId = httpContext.RequestServices
            .GetRequiredService<ICorrelationContext>().CorrelationId;

        _logger.LogError(
            exception, "Unhandled exception. CorrelationId={CorrelationId}", correlationId);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(
            new
            {
                title = "An unexpected error occurred.",
                status = StatusCodes.Status500InternalServerError,
                detail = "The server encountered an unexpected condition and could not complete the request.",
                code = "internal_error",
                correlationId,
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }
}
