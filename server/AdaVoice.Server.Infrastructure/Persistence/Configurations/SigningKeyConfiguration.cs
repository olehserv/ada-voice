using AdaVoice.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdaVoice.Server.Infrastructure.Persistence.Configurations;

internal sealed class SigningKeyConfiguration : IEntityTypeConfiguration<SigningKey>
{
    public void Configure(EntityTypeBuilder<SigningKey> builder)
    {
        builder.Property(x => x.Id).HasValueGenerator<UuidV7ValueGenerator>().ValueGeneratedOnAdd();

        builder.Property(x => x.Status).HasConversion(StatusConverters.SigningKeyStatus);
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_signing_keys_status", "status IN ('active', 'next', 'retired')"));

        // byte[] maps to bytea by default; being explicit documents the intent.
        builder.Property(x => x.PrivateKeyEncrypted).HasColumnType("bytea");

        // §2 declares signing_keys.kid Unique; it goes into the JWS header.
        builder.HasIndex(x => x.Kid).IsUnique();
    }
}
