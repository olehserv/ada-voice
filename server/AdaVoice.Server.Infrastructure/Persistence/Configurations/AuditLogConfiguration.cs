using AdaVoice.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdaVoice.Server.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(x => x.Id).HasValueGenerator<UuidV7ValueGenerator>().ValueGeneratedOnAdd();

        builder.Property(x => x.ActorType).HasConversion(StatusConverters.ActorType);
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_audit_logs_actor_type", "actor_type IN ('user', 'system', 'admin')"));

        builder.Property(x => x.Data).HasColumnType("jsonb");

        // Admin audit screens filter by tenant + date. No global query filter here
        // (tenant is nullable for system rows; scoping is done explicitly at query sites).
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
    }
}
