using AdaVoice.Server.Api.Infrastructure;
using AdaVoice.Server.Domain.Enums;
using AdaVoice.Server.Infrastructure.Auth;
using AdaVoice.Server.Infrastructure.Persistence;
using AdaVoice.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdaVoice.Server.Tests.Auth;

/// <summary>Exercises the audit batching pieces (<see cref="AuditQueue"/>,
/// <see cref="AuditWriter"/>, <see cref="AuditFlushService"/>) directly against a real
/// PostgreSQL database, without going through the HTTP host — the behaviour under test is the
/// batching mechanics themselves, not any particular endpoint.</summary>
[Trait("Category", "Integration")]
[Collection(ServerIntegrationCollection.Name)]
public sealed class AuditBatchingTests
{
    private readonly PostgresFixture _fixture;

    public AuditBatchingTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Enqueued_entries_flush_in_one_batch_with_the_enqueue_time_as_CreatedAt()
    {
        var options = new AuditBatchingOptions { FlushIntervalSeconds = 1, MaxBatchSize = 500, QueueCapacity = 1_000 };
        var queue = new AuditQueue(options);
        var writer = new AuditWriter(queue, new CorrelationContext { CorrelationId = "batch-test" });

        const int count = 25;
        const string action = "test.audit_batch";
        var beforeEnqueue = DateTimeOffset.UtcNow;
        for (var i = 0; i < count; i++)
        {
            await writer.WriteAsync(
                new AuditEntry { Action = action, EntityType = "test", ActorType = ActorType.System },
                CancellationToken.None);
        }

        var afterEnqueue = DateTimeOffset.UtcNow;

        // A deliberate gap between enqueue and flush: if the interceptor (or the service)
        // stamped "now" at flush time instead of honouring the enqueue-time CreatedAt, every
        // row would land noticeably after afterEnqueue and the assertion below would catch it.
        await Task.Delay(TimeSpan.FromSeconds(2));

        await using var provider = BuildScopeProvider();
        var flushService = CreateFlushService(queue, options, provider);
        await flushService.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(1_500)); // let the 1s periodic timer fire once
        await flushService.StopAsync(CancellationToken.None);

        await using var verify = _fixture.CreateContext(new AmbientTenantProvider());
        var rows = await verify.AuditLogs.Where(a => a.Action == action).ToListAsync();

        Assert.Equal(count, rows.Count); // one/few SaveChanges calls persisted every enqueued row
        Assert.All(rows, row => Assert.InRange(row.CreatedAt, beforeEnqueue.AddSeconds(-1), afterEnqueue.AddSeconds(1)));
    }

    [Fact]
    public async Task StopAsync_flushes_remaining_entries_via_the_shutdown_drain_not_the_periodic_tick()
    {
        // An hour-long interval guarantees the periodic timer cannot possibly fire during this
        // test, so any persisted row can only have come from StopAsync's final drain — isolating
        // that path, which is what actually protects a security-audit row on graceful shutdown.
        var options = new AuditBatchingOptions { FlushIntervalSeconds = 3600, MaxBatchSize = 500, QueueCapacity = 100 };
        var queue = new AuditQueue(options);
        var writer = new AuditWriter(queue, new CorrelationContext { CorrelationId = "shutdown-test" });

        const int count = 5;
        const string action = "test.audit_shutdown_flush";
        for (var i = 0; i < count; i++)
        {
            await writer.WriteAsync(
                new AuditEntry { Action = action, EntityType = "test", ActorType = ActorType.System },
                CancellationToken.None);
        }

        await using var provider = BuildScopeProvider();
        var flushService = CreateFlushService(queue, options, provider);
        await flushService.StartAsync(CancellationToken.None);
        await flushService.StopAsync(CancellationToken.None); // must drain here — the next tick is an hour away

        await using var verify = _fixture.CreateContext(new AmbientTenantProvider());
        var rows = await verify.AuditLogs.Where(a => a.Action == action).ToListAsync();

        Assert.Equal(count, rows.Count);
    }

    private ServiceProvider BuildScopeProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _fixture.CreateContext(new AmbientTenantProvider()));
        return services.BuildServiceProvider();
    }

    private static AuditFlushService CreateFlushService(
        IAuditQueue queue, AuditBatchingOptions options, ServiceProvider provider) =>
        new(queue, provider.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<AuditFlushService>.Instance);
}
