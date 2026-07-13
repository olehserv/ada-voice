using AdaVoice.Server.Infrastructure.Persistence;
using AdaVoice.Server.Infrastructure.Persistence.Seeding;
using AdaVoice.Server.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AdaVoice.Server.Tests.Persistence;

// AC3 + §14 pitfall #19: the seeder is idempotent (no duplicate tenant/plan/super_admin across
// repeated runs) and the seeded super_admin password is never a default, never logged, hashed
// with ASP.NET Core Identity's PasswordHasher, and has LastLoginAt == null so Phase 2's login
// flow can force a password change. Real PostgreSQL, so tagged Integration.
[Trait("Category", "Integration")]
public sealed class SeederTests : IClassFixture<PostgresFixture>
{
    private const string StandardPlanCode = "standard";

    private readonly PostgresFixture _fixture;

    public SeederTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SeedAsync_run_twice_creates_no_duplicates()
    {
        var options = new SuperAdminSeedOptions
        {
            Email = "idempotent@example.com",
            Password = "Idempotent-Pass!7",
        };

        await using (var first = _fixture.CreateContext(SystemProvider()))
        {
            await CreateSeeder(first).SeedAsync(options);
        }

        await using (var second = _fixture.CreateContext(SystemProvider()))
        {
            await CreateSeeder(second).SeedAsync(options);
        }

        await using var verify = _fixture.CreateContext(SystemProvider());
        Assert.Equal(1, await verify.Tenants.CountAsync(t => t.Id == DatabaseSeeder.SystemTenantId));
        Assert.Equal(1, await verify.Plans.CountAsync(p => p.Code == StandardPlanCode));
        Assert.Equal(1, await verify.Users.IgnoreQueryFilters().CountAsync(u => u.Email == options.Email));
    }

    [Fact]
    public async Task Seeded_super_admin_password_hash_verifies_and_is_not_plaintext()
    {
        var options = new SuperAdminSeedOptions
        {
            Email = "hash-check@example.com",
            Password = "Str0ng!Passw0rd#1",
        };

        await using var ctx = _fixture.CreateContext(SystemProvider());
        await CreateSeeder(ctx).SeedAsync(options);

        var user = await ctx.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == options.Email);

        Assert.NotEqual(options.Password, user.PasswordHash);

        var hasher = new PasswordHasher<User>();
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, options.Password);
        Assert.Equal(PasswordVerificationResult.Success, result);
    }

    [Fact]
    public async Task FromEnvironment_returns_null_when_password_missing_and_seed_creates_no_super_admin()
    {
        var env = new Dictionary<string, string?>
        {
            ["ADAVOICE_SEED_SUPERADMIN_EMAIL"] = "no-password@example.com",
        };

        Assert.Null(SuperAdminSeedOptions.FromEnvironment(env));

        await using var ctx = _fixture.CreateContext(SystemProvider());
        var usersBefore = await ctx.Users.IgnoreQueryFilters().CountAsync();

        var logger = new CapturingLogger<DatabaseSeeder>();
        await new DatabaseSeeder(ctx, new PasswordHasher<User>(), logger).SeedAsync(null);

        var usersAfter = await ctx.Users.IgnoreQueryFilters().CountAsync();
        Assert.Equal(usersBefore, usersAfter);

        // Tenant + plan are still seeded even when super_admin is skipped.
        Assert.True(await ctx.Tenants.AnyAsync(t => t.Id == DatabaseSeeder.SystemTenantId));
        Assert.True(await ctx.Plans.AnyAsync(p => p.Code == StandardPlanCode));

        Assert.Contains(logger.Messages, m => m.Contains("super_admin seed skipped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Password_is_never_logged()
    {
        var options = new SuperAdminSeedOptions
        {
            Email = "no-log@example.com",
            Password = "SuperSecretDoNotLog-42!",
        };

        await using var ctx = _fixture.CreateContext(SystemProvider());
        var logger = new CapturingLogger<DatabaseSeeder>();

        await new DatabaseSeeder(ctx, new PasswordHasher<User>(), logger).SeedAsync(options);

        Assert.DoesNotContain(logger.Messages, m => m.Contains(options.Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Seeded_super_admin_has_null_last_login_at()
    {
        var options = new SuperAdminSeedOptions
        {
            Email = "last-login@example.com",
            Password = "AnotherStrongPass!9",
        };

        await using var ctx = _fixture.CreateContext(SystemProvider());
        await CreateSeeder(ctx).SeedAsync(options);

        var user = await ctx.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == options.Email);
        Assert.Null(user.LastLoginAt);
    }

    private static AmbientTenantProvider SystemProvider() => new() { CurrentTenantId = null };

    private static DatabaseSeeder CreateSeeder(AdaVoiceDbContext ctx) =>
        new(ctx, new PasswordHasher<User>(), new CapturingLogger<DatabaseSeeder>());

    /// <summary>Minimal <see cref="ILogger{TCategoryName}"/> that records every formatted
    /// message, so #19c can assert the plaintext password never appears in any of them.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
