using System.Text.Json;
using AdaVoice.Server.Api.Infrastructure;
using AdaVoice.Server.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdaVoice.Server.Tests.Auth;

public class ExceptionHandlingTests
{
    [Fact]
    public async Task Handler_writes_generic_problem_without_exception_text()
    {
        var ctx = new DefaultHttpContext();
        var body = new MemoryStream();
        ctx.Response.Body = body;
        // The handler resolves the request-scoped correlation context from RequestServices.
        var correlation = new CorrelationContext { CorrelationId = "corr-123" };
        var services = new ServiceCollection();
        services.AddSingleton<ICorrelationContext>(correlation);
        ctx.RequestServices = services.BuildServiceProvider();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        var secret = "SUPER-SECRET-STACK-marker";
        var handled = await handler.TryHandleAsync(ctx, new InvalidOperationException(secret), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(500, ctx.Response.StatusCode);
        Assert.StartsWith("application/problem+json", ctx.Response.ContentType);
        body.Position = 0;
        var json = await new StreamReader(body).ReadToEndAsync();
        Assert.DoesNotContain(secret, json);
        Assert.DoesNotContain("InvalidOperationException", json);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("corr-123", doc.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal("internal_error", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Middleware_generates_and_echoes_correlation_id()
    {
        var ctx = new DefaultHttpContext();
        var correlation = new CorrelationContext();
        var mw = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await mw.InvokeAsync(ctx, correlation);

        Assert.False(string.IsNullOrWhiteSpace(correlation.CorrelationId));
        Assert.Equal(correlation.CorrelationId, ctx.Response.Headers["X-Correlation-Id"].ToString());
    }
}
