using AdaVoice.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdaVoice.Server.Infrastructure.Persistence.Configurations;

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.Property(x => x.Id).HasValueGenerator<UuidV7ValueGenerator>().ValueGeneratedOnAdd();

        builder.Property(x => x.PriceUah).HasColumnType("numeric(12,2)");
        builder.Property(x => x.Features).HasColumnType("jsonb");

        // §2 declares plans.code Unique; the Task-4 seeder checks existence by code.
        builder.HasIndex(x => x.Code).IsUnique();
    }
}
