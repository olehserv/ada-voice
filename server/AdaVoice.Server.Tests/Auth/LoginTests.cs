using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdaVoice.Server.Api.Auth;
using AdaVoice.Server.Domain.Entities;
using AdaVoice.Server.Domain.Enums;
using AdaVoice.Server.Infrastructure.Persistence;
using AdaVoice.Server.Tests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AdaVoice.Server.Tests.Auth;

/// <summary>AC-relevant login coverage: happy path (tokens issued, audited, refresh stored as a
/// hash, last_login stamped), wrong password (generic 401 + counter), and unknown email
/// returning a byte-identical response to a wrong password (§14 #4 user enumeration).</summary>
[Trait("Category", "Integration")]
[Collection(ServerIntegrationCollection.Name)]
public sealed class LoginTests
{
    private const string Password = "CorrectHorseBatteryStaple1!";
    private readonly PostgresFixture _fixture;

    public LoginTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Login_with_correct_password_returns_tokens_audits_success_and_stores_hash()
    {
        var (_, userId, email) = await SeedActiveUserAsync();
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { email, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(body.RefreshToken));

        // The access token validates against the server's public key.
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(
            body.AccessToken,
            new TokenValidationParameters
            {
                ValidIssuer = "adavoice-auth",
                ValidAudience = "adavoice-api",
                IssuerSigningKey = factory.PublicSigningKey,
                ValidAlgorithms = ["ES256"],
                ClockSkew = TimeSpan.FromMinutes(1),
            });
        Assert.True(validation.IsValid);

        await using var ctx = _fixture.CreateContext(new AmbientTenantProvider());
        var stored = await ctx.RefreshTokens.SingleAsync(r => r.UserId == userId);
        Assert.NotEqual(body.RefreshToken, stored.TokenHash); // only the hash is persisted
        var user = await ctx.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId);
        Assert.NotNull(user.LastLoginAt);
        Assert.Equal(0, user.FailedLoginCount);
        var actions = await ctx.AuditLogs.Where(a => a.ActorUserId == userId).Select(a => a.Action).ToListAsync();
        Assert.Contains("auth.login_succeeded", actions);
    }

    [Fact]
    public async Task Wrong_password_returns_generic_401_increments_counter_and_audits_failure()
    {
        var (_, userId, email) = await SeedActiveUserAsync();
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { email, password = "definitely-wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_credentials", await ReadCodeAsync(response));

        await using var ctx = _fixture.CreateContext(new AmbientTenantProvider());
        var user = await ctx.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId);
        Assert.Equal(1, user.FailedLoginCount);
        var actions = await ctx.AuditLogs.Where(a => a.ActorUserId == userId).Select(a => a.Action).ToListAsync();
        Assert.Contains("auth.login_failed", actions);
    }

    [Fact]
    public async Task Unknown_email_returns_a_response_identical_to_a_wrong_password()
    {
        var (_, _, email) = await SeedActiveUserAsync();
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync(
            "/api/auth/login", new { email, password = "definitely-wrong" });
        var unknownEmail = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = $"nobody-{Guid.NewGuid():N}@example.com", password = "definitely-wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(wrongPassword.StatusCode, unknownEmail.StatusCode);
        // Bodies must be identical except the per-request correlationId (§14 #4).
        Assert.Equal(
            await NormalizeAsync(wrongPassword),
            await NormalizeAsync(unknownEmail));
    }

    private async Task<(Guid tenantId, Guid userId, string email)> SeedActiveUserAsync()
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

        return (tenant.Id, user.Id, email);
    }

    private static async Task<string> ReadCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
    }

    /// <summary>Serializes the problem body with the per-request diagnostic fields
    /// (correlationId, traceId) removed, so two responses can be compared for identical
    /// shape/content. Those fields vary on every request regardless of outcome, so they carry
    /// no account-enumeration signal.</summary>
    private static async Task<string> NormalizeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var pairs = doc.RootElement.EnumerateObject()
            .Where(p => p.Name is not ("correlationId" or "traceId"))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value.GetRawText()}");
        return string.Join("|", pairs);
    }
}
