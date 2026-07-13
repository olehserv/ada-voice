using AdaVoice.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdaVoice.Server.Infrastructure.Persistence.Configurations;

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.Property(x => x.Id).HasValueGenerator<UuidV7ValueGenerator>().ValueGeneratedOnAdd();

        builder.Property(x => x.Status).HasConversion(StatusConverters.SubscriptionStatus);
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_subscriptions_status",
            "status IN ('trial', 'active', 'past_due', 'grace_period', 'suspended', 'cancelled', 'expired')"));

        // One active subscription per tenant: partial unique that ignores terminal states.
        builder.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasFilter("status NOT IN ('cancelled', 'expired')");

        // FKs → tenants, plans (both required). No-navigation overload; conservative Restrict.
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
