using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdaVoice.Server.Domain.Entities;
using AdaVoice.Server.Domain.Enums;
using AdaVoice.Server.Infrastructure.Persistence;
using AdaVoice.Server.Tests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AdaVoice.Server.Tests.Auth;

/// <summary>Account lockout: AC2 (11th attempt in the window is refused; a correct password
/// during lockout still fails) plus §14 #5 (atomic counter), the SEC-03 generic-response parity
/// for the locked case, and lockout re-arming after the window expires.</summary>
[Trait("Category", "Integration")]
[Collection(ServerIntegrationCollection.Name)]
public sealed class LockoutTests
{
    private const string Password = "CorrectHorseBatteryStaple1!";
    private const int MaxFailedLogins = 10; // matches appsettings Auth:MaxFailedLogins
    private readonly PostgresFixture _fixture;

    public LockoutTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Lockout_triggers_at_the_threshold_audits_once_and_a_correct_password_still_fails()
    {
        var (userId, email) = await SeedActiveUserAsync();
        // High rate-limit permit so the burst of attempts is not throttled before lockout.
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        for (var i = 0; i < MaxFailedLogins; i++)
        {
            var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        await using (var ctx = _fixture.CreateContext(new AmbientTenantProvider()))
        {
            var user = await ctx.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId);
            Assert.NotNull(user.LockedUntil); // locked
            var lockedAudits = await ctx.AuditLogs
                .Where(a => a.ActorUserId == userId && a.Action == "auth.account_locked").CountAsync();
            Assert.Equal(1, lockedAudits); // audited exactly once
        }

        // A CORRECT password while locked still fails, and fails identically to a wrong password.
        var correctWhileLocked = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, correctWhileLocked.StatusCode);
        Assert.Equal("invalid_credentials", await ReadCodeAsync(correctWhileLocked));
    }

    [Fact]
    public async Task Locked_account_response_is_identical_to_a_wrong_password_response()
    {
        var (_, email) = await SeedActiveUserAsync();
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        // Baseline wrong-password body (before lockout).
        var wrongBefore = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong" });
        var wrongBody = await NormalizeAsync(wrongBefore);

        // Drive to lockout, then present the CORRECT password (locked).
        for (var i = 0; i < MaxFailedLogins; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong" });
        }
        var lockedResponse = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });

        Assert.Equal(wrongBefore.StatusCode, lockedResponse.StatusCode);
        Assert.Equal(wrongBody, await NormalizeAsync(lockedResponse)); // no "locked"/"lockedUntil" leak
    }

    [Fact]
    public async Task Lockout_rearms_after_the_window_expires()
    {
        var (userId, email) = await SeedActiveUserAsync();
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        // Simulate a just-expired lockout: counter at the threshold, locked_until in the past.
        await using (var ctx = _fixture.CreateContext(new AmbientTenantProvider()))
        {
            await ctx.Users.IgnoreQueryFilters()
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.FailedLoginCount, MaxFailedLogins)
                    .SetProperty(u => u.LockedUntil, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        // One more failed attempt after expiry must re-arm the lock (fresh future locked_until).
        var afterExpiry = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, afterExpiry.StatusCode);

        await using var check = _fixture.CreateContext(new AmbientTenantProvider());
        var user = await check.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId);
        Assert.NotNull(user.LockedUntil);
        Assert.True(user.LockedUntil > DateTimeOffset.UtcNow); // re-armed into the future
    }

    [Fact]
    public async Task Parallel_wrong_passwords_increment_the_counter_atomically()
    {
        const int attempts = 8; // below the threshold so no lock interferes with the count
        var (userId, email) = await SeedActiveUserAsync();
        await using var factory = new AuthApiFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        var tasks = Enumerable.Range(0, attempts)
            .Select(_ => client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong" }));
        await Task.WhenAll(tasks);

        await using var ctx = _fixture.CreateContext(new AmbientTenantProvider());
        var user = await ctx.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId);
        Assert.Equal(attempts, user.FailedLoginCount); // no lost updates (§14 #5)
    }

    private async Task<(Guid userId, string email)> SeedActiveUserAsync()
    {
        var email = $"lock-{Guid.NewGuid():N}@example.com";
        await using var ctx = _fixture.CreateContext(new AmbientTenantProvider { CurrentTenantId = null });
        var tenant = new Tenant { Name = "Acme", Status = TenantStatus.Active, ContactEmail = "acme@example.com" };
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();
        var user = new User { TenantId = tenant.Id, Email = email, Role = UserRole.Operator, Status = UserStatus.Active };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return (user.Id, email);
    }

    private static async Task<string> ReadCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
    }

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
