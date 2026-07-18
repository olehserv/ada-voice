using System.Threading.RateLimiting;
using AdaVoice.Server.Infrastructure.Auth;
using Microsoft.AspNetCore.Mvc;

namespace AdaVoice.Server.Api.Infrastructure;

/// <summary>Per-IP fixed-window rate limiting for <c>/api/auth/*</c> (security-design.md §8).
/// Rejections return RFC 7807 <c>429 rate_limited</c> with <c>Retry-After</c>.</summary>
public static class AuthRateLimit
{
    public const string PolicyName = "auth";

    public static IServiceCollection AddAuthRateLimiter(this IServiceCollection services, int permitPerMinute)
    {
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(PolicyName, httpContext =>
            {
                // §14 #22: partition by the connection's remote IP only. Trusting X-Forwarded-For
                // needs ForwardedHeaders with KnownProxies/KnownNetworks configured for the real
                // deployment topology — that is Phase-10 work; do not read forwarded headers here.
                var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });

            options.OnRejected = async (context, ct) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;

                var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? (int)retryAfter.TotalSeconds
                    : 60;
                response.Headers.RetryAfter = retryAfterSeconds.ToString();

                var correlationId = context.HttpContext.RequestServices
                    .GetRequiredService<ICorrelationContext>().CorrelationId;

                var problem = new ProblemDetails
                {
                    Type = "https://adavoice.example/problems/rate_limited",
                    Title = "Too many requests",
                    Status = StatusCodes.Status429TooManyRequests,
                    Detail = "Too many requests. Please retry after a short wait.",
                    Extensions =
                    {
                        ["code"] = "rate_limited",
                        ["correlationId"] = correlationId,
                    },
                };

                await response.WriteAsJsonAsync(
                    problem,
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: ct);
            };
        });

        return services;
    }
}
