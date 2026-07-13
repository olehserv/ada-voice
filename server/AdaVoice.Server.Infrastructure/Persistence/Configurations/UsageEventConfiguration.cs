using AdaVoice.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdaVoice.Server.Infrastructure.Persistence.Configurations;

internal sealed class UsageEventConfiguration : IEntityTypeConfiguration<UsageEvent>
{
    public void Configure(EntityTypeBuilder<UsageEvent> builder)
    {
        builder.Property(x => x.Id).HasValueGenerator<UuidV7ValueGenerator>().ValueGeneratedOnAdd();

        builder.Property(x => x.Data).HasColumnType("jsonb");

        // Usage summaries per period.
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt });
    }
}
