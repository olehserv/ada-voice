using AdaVoice.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdaVoice.Server.Infrastructure.Persistence.Configurations;

internal sealed class DeviceActivationConfiguration : IEntityTypeConfiguration<DeviceActivation>
{
    public void Configure(EntityTypeBuilder<DeviceActivation> builder)
    {
        builder.Property(x => x.Id).HasValueGenerator<UuidV7ValueGenerator>().ValueGeneratedOnAdd();

        builder.Property(x => x.Status).HasConversion(StatusConverters.DeviceStatus);
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_device_activations_status", "status IN ('active', 'revoked', 'blocked', 'expired')"));

        // One activation row per device; re-activation updates it.
        builder.HasIndex(x => new { x.TenantId, x.DeviceId }).IsUnique();

        // Device-limit check counts active devices per tenant.
        builder.HasIndex(x => new { x.TenantId, x.Status });

        // FKs → tenants, users (both required). No-navigation overload; conservative Restrict.
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
