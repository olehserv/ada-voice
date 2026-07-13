using AdaVoice.Server.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AdaVoice.Server.Infrastructure.Persistence;

/// <summary>Stamps audit timestamps and the tenant id on save. This is the SINGLE shared
/// place tenant_id is assigned — never from a caller-supplied value (§14 pitfall #16).
/// Added rows get CreatedAt/UpdatedAt; modified rows get UpdatedAt; tenant-owned rows with
/// an unset tenant get the ambient tenant. Append-only entities that only implement
/// <see cref="IHasCreatedAt"/> never have an UpdatedAt touched.</summary>
public sealed class AuditableTenantInterceptor : SaveChangesInterceptor
{
    private readonly ITenantProvider _tenantProvider;

    public AuditableTenantInterceptor(ITenantProvider tenantProvider) =>
        _tenantProvider = tenantProvider;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Compute the timestamp once per save so all rows in the batch share it.
        var now = DateTimeOffset.UtcNow;
        var tenantId = _tenantProvider.CurrentTenantId;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity is IHasCreatedAt created)
                    {
                        created.CreatedAt = now;
                    }

                    if (entry.Entity is IHasTimestamps addedStamps)
                    {
                        addedStamps.UpdatedAt = now;
                    }

                    // Only stamp when we actually have a tenant; without the guard a null
                    // context would silently write an all-zeros GUID.
                    if (entry.Entity is IHasTenant owned
                        && owned.TenantId == Guid.Empty
                        && tenantId is Guid current)
                    {
                        owned.TenantId = current;
                    }

                    break;

                case EntityState.Modified:
                    if (entry.Entity is IHasTimestamps modifiedStamps)
                    {
                        modifiedStamps.UpdatedAt = now;
                    }

                    break;
            }
        }
    }
}
