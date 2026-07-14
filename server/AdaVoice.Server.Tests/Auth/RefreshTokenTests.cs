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

/// <summary>Refresh-token rotation, reuse detection (AC1 + §14 #3), and logout family
/// revocation.</summary>
[Trait("Category", "Integration")]
[Collection(ServerIntegrationCollection.Name)]
public sealed class RefreshTokenTests
{
    private const string Password = "CorrectHorseBatteryStaple1!";
    private readonly PostgresFixture _fixture;

    public RefreshTokenTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Refresh_rotates_and_replaying_the_old_token_revokes_the_whole_family()
    {
        var email = await SeedActiveUserAsync();
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        var original = (await LoginAsync(client, email))!;
        var rotated = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = original.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var newTokens = await rotated.Content.ReadFromJsonAsync<TokenResponse>();

        // Replay the ORIGINAL (now-rotated) token: reuse tripwire → 401 and the family is revoked.
        var replay = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = original.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // The token issued by the successful rotation is now dead too (whole family revoked).
        var afterRevoke = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = newTokens!.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
    }

    [Fact]
    public async Task Two_concurrent_refreshes_of_the_same_token_yield_exactly_one_success()
    {
        var email = await SeedActiveUserAsync();
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();
        var original = (await LoginAsync(client, email))!;

        var first = client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = original.RefreshToken });
        var second = client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = original.RefreshToken });
        var responses = await Task.WhenAll(first, second);

        var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var unauthorizedCount = responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized);
        Assert.Equal(1, okCount);
        Assert.Equal(1, unauthorizedCount);
    }

    [Fact]
    public async Task Logout_revokes_the_family_so_the_refresh_token_stops_working()
    {
        var email = await SeedActiveUserAsync();
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();
        var tokens = (await LoginAsync(client, email))!;

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { refreshToken = tokens.RefreshToken }),
        };
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var logoutResponse = await client.SendAsync(logout);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Reuse_detection_writes_an_audit_row()
    {
        var email = await SeedActiveUserAsync();
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();
        var original = (await LoginAsync(client, email))!;

        await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = original.RefreshToken });
        await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = original.RefreshToken }); // reuse

        await using var ctx = _fixture.CreateContext(new AmbientTenantProvider());
        var actions = await ctx.AuditLogs.Select(a => a.Action).ToListAsync();
        Assert.Contains("auth.refresh_reuse_detected", actions);
    }

    private static async Task<TokenResponse?> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TokenResponse>();
    }

    private async Task<string> SeedActiveUserAsync()
    {
        var email = $"op-{Guid.NewGuid():N}@example.com";
        await using var ctx = _fixture.CreateContext(new AmbientTenantProvider { CurrentTenantId = null });

        var tenant = new Tenant { Name = "Acme", Status = TenantStatus.Active, ContactEmail = "acme@example.com" };
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();

        var user = new User
        {
            TenantId = tenant.Id,
            Email = email,
            Role = UserRole.TenantAdmin,
            Status = UserStatus.Active,
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        return email;
    }
}
