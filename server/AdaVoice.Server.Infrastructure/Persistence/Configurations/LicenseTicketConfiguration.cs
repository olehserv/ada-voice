using AdaVoice.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdaVoice.Server.Infrastructure.Persistence.Configurations;

internal sealed class LicenseTicketConfiguration : IEntityTypeConfiguration<LicenseTicket>
{
    public void Configure(EntityTypeBuilder<LicenseTicket> builder)
    {
        // PK is the ticket's jti claim, not an Id column.
        builder.HasKey(x => x.Jti);
        builder.Property(x => x.Jti).HasValueGenerator<UuidV7ValueGenerator>().ValueGeneratedOnAdd();

        builder.Property(x => x.Status).HasConversion(StatusConverters.LicenseTicketStatus);
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_license_tickets_status", "status IN ('issued', 'revoked')"));

        // TicketCleanupJob range scan; revocation checks by activation + status.
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasIndex(x => new { x.DeviceActivationId, x.Status });
    }
}
