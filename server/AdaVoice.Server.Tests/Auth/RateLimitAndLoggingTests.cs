using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AdaVoice.Server.Api.Auth;
using AdaVoice.Server.Domain.Entities;
using AdaVoice.Server.Domain.Enums;
using AdaVoice.Server.Infrastructure.Persistence;
using AdaVoice.Server.Tests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AdaVoice.Server.Tests.Auth;

/// <summary>Rate limiting (§14 #22), no secret in logs (§14 #6), and audit completeness (AC4).</summary>
[Trait("Category", "Integration")]
[Collection(ServerIntegrationCollection.Name)]
public sealed class RateLimitAndLoggingTests
{
    private const string Password = "CorrectHorseBatteryStaple1!";
    private readonly PostgresFixture _fixture;

    public RateLimitAndLoggingTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Exceeding_the_auth_window_returns_429_with_retry_after()
    {
        var email = await SeedActiveUserAsync();
        await using var factory = new AuthApiFactory(_fixture.ConnectionString, authPermitPerMinute: 3);
        var client = factory.CreateClient();

        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 5; i++)
        {
            responses.Add(await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong" }));
        }

        var limited = responses.FirstOrDefault(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.NotNull(limited); // at least one request past the permit is throttled (#22)
        Assert.Equal("rate_limited", await ReadCodeAsync(limited!));
        Assert.NotNull(limited!.Headers.RetryAfter);
    }

    [Fact]
    public async Task No_password_or_token_appears_in_the_logs()
    {
        var email = await SeedActiveUserAsync();
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        var tokens = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });

        var logs = factory.LogMessages.ToArray();
        Assert.DoesNotContain(logs, m => m.Contains(Password, StringComparison.Ordinal));
        Assert.DoesNotContain(logs, m => m.Contains(tokens.RefreshToken, StringComparison.Ordinal));
        Assert.DoesNotContain(logs, m => m.Contains(tokens.AccessToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_auth_event_type_is_audited()
    {
        // A user for the success/refresh/reuse/logout/change-password events...
        var email = await SeedActiveUserAsync();
        // ...and a separate user driven to lockout, so locking does not block the first user's flow.
        var lockEmail = await SeedActiveUserAsync();

        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        // login_failed + login_succeeded
        await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong" });
        var tokens = (await (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }))
            .Content.ReadFromJsonAsync<TokenResponse>())!;

        // token_refreshed, then refresh_reuse_detected (replay the original)
        await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });

        // A fresh session to log out and to change the password.
        var session = (await (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }))
            .Content.ReadFromJsonAsync<TokenResponse>())!;
        await SendWithBearerAsync(client, HttpMethod.Post, "/api/auth/logout", session.AccessToken,
            new { refreshToken = session.RefreshToken });

        var pwSession = (await (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }))
            .Content.ReadFromJsonAsync<TokenResponse>())!;
        await SendWithBearerAsync(client, HttpMethod.Post, "/api/auth/change-password", pwSession.AccessToken,
            new { currentPassword = Password, newPassword = "AnotherSecret9!" });

        // account_locked (second user)
        for (var i = 0; i < 10; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new { email = lockEmail, password = "wrong" });
        }

        await using var ctx = _fixture.CreateContext(new AmbientTenantProvider());
        var actions = await ctx.AuditLogs.Select(a => a.Action).Distinct().ToListAsync();
        foreach (var expected in new[]
        {
            "auth.login_failed", "auth.login_succeeded", "auth.token_refreshed",
            "auth.refresh_reuse_detected", "auth.logout", "auth.password_changed", "auth.account_locked",
        })
        {
            Assert.Contains(expected, actions);
        }
    }

    private static async Task SendWithBearerAsync(
        HttpClient client, HttpMethod method, string uri, string accessToken, object body)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await client.SendAsync(request);
    }

    private static async Task<string> ReadCodeAsync(HttpResponseMessage response)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
    }

    private async Task<string> SeedActiveUserAsync()
    {
        var email = $"rl-{Guid.NewGuid():N}@example.com";
        await using var ctx = _fixture.CreateContext(new AmbientTenantProvider { CurrentTenantId = null });
        var tenant = new Tenant { Name = "Acme", Status = TenantStatus.Active, ContactEmail = "acme@example.com" };
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();
        var user = new User { TenantId = tenant.Id, Email = email, Role = UserRole.Operator, Status = UserStatus.Active };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return email;
    }
}
