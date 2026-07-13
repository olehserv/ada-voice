using AdaVoice.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdaVoice.Server.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.Property(x => x.Id).HasValueGenerator<UuidV7ValueGenerator>().ValueGeneratedOnAdd();

        builder.Property(x => x.ResponseBody).HasColumnType("jsonb");

        // Retry safety: one stored response per (key, endpoint); cleanup scans by expiry.
        builder.HasIndex(x => new { x.Key, x.Endpoint }).IsUnique();
        builder.HasIndex(x => x.ExpiresAt);
    }
}
