using AdaVoice.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdaVoice.Server.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.Property(x => x.Id).HasValueGenerator<UuidV7ValueGenerator>().ValueGeneratedOnAdd();

        // Hot lookup on every refresh; family index supports family revocation.
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.FamilyId);

        // FK → users (required). No-navigation overload; conservative Restrict.
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK → device_activations, nullable (admin-panel logins have no device).
        builder.HasOne<DeviceActivation>().WithMany().HasForeignKey(x => x.DeviceActivationId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Self-referential FK → refresh_tokens, nullable (rotation link). Restrict.
        builder.HasOne<RefreshToken>().WithMany().HasForeignKey(x => x.ReplacedById)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
