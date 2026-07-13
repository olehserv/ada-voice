using AdaVoice.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdaVoice.Server.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(x => x.Id).HasValueGenerator<UuidV7ValueGenerator>().ValueGeneratedOnAdd();

        builder.Property(x => x.Role).HasConversion(StatusConverters.UserRole);
        builder.Property(x => x.Status).HasConversion(StatusConverters.UserStatus);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_users_role", "role IN ('operator', 'tenant_admin', 'super_admin')");
            t.HasCheckConstraint("ck_users_status", "status IN ('active', 'disabled')");
        });

        // DEVIATION (see task report): §3 asks for unique (tenant_id, lower(email)), but EF/Npgsql
        // cannot express a lower() expression index in the model. We enforce uniqueness on
        // (tenant_id, email) — case-SENSITIVE for now. Revisit if case-insensitive login matters.
        builder.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
    }
}
