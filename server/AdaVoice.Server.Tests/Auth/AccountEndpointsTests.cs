using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AdaVoice.Server.Api.Auth;
using AdaVoice.Server.Domain.Entities;
using AdaVoice.Server.Domain.Enums;
using AdaVoice.Server.Tests.Persistence;
using Microsoft.AspNetCore.Identity;

namespace AdaVoice.Server.Tests.Auth;

/// <summary>change-password (revokes existing sessions), /me, and access-token expiry (AC3 / §14 #1).</summary>
[Trait("Category", "Integration")]
[Collection(ServerIntegrationCollection.Name)]
public sealed class AccountEndpointsTests
{
    private const string Password = "CorrectHorseBatteryStaple1!";
    private readonly PostgresFixture _fixture;

    public AccountEndpointsTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Change_password_sets_the_new_hash_and_revokes_existing_sessions()
    {
        var email = await SeedActiveUserAsync();
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();
        var tokens = (await LoginAsync(client, email, Password))!;

        const string newPassword = "BrandNewSecret2!";
        using var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = Password, newPassword }),
        };
        change.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var changeResponse = await client.SendAsync(change);
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        // Old refresh token is now revoked; login works only with the NEW password.
        var oldRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        Assert.NotNull(await LoginAsync(client, email, newPassword));
    }

    [Fact]
    public async Task Change_password_with_a_wrong_current_password_is_401()
    {
        var email = await SeedActiveUserAsync();
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();
        var tokens = (await LoginAsync(client, email, Password))!;

        using var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = "not-the-password", newPassword = "Whatever3!" }),
        };
        change.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var response = await client.SendAsync(change);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_returns_the_current_user()
    {
        var email = await SeedActiveUserAsync();
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();
        var tokens = (await LoginAsync(client, email, Password))!;

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var response = await client.SendAsync(me);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(body);
        Assert.Equal(email, body!.Email);
        Assert.Equal("operator", body.Role);
    }

    [Fact]
    public async Task Me_without_a_token_is_401()
    {
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_access_token_is_rejected()
    {
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();
        // Signed with the server's key but expired 5 minutes ago — beyond the 1-minute skew (§14 #1).
        var expired = factory.CreateAccessToken(
            Guid.NewGuid(), Guid.NewGuid(), "operator", DateTime.UtcNow.AddMinutes(-5));

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expired);
        var response = await client.SendAsync(me);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<TokenResponse?> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TokenResponse>();
    }

    private async Task<string> SeedActiveUserAsync()
    {
        var email = $"acct-{Guid.NewGuid():N}@example.com";
        await using var ctx = _fixture.CreateContext(new AdaVoice.Server.Infrastructure.Persistence.AmbientTenantProvider { CurrentTenantId = null });
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
