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

        // Email is citext (see AdaVoiceDbContext.OnModelCreating), so this unique index on
        // (tenant_id, email) compares case-insensitively — it enforces §3's
        // unique (tenant_id, lower(email)) without a raw lower() expression index.
        builder.Property(x => x.Email).HasColumnType("citext");
        builder.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();

        // FK → tenants (required). No-navigation overload keeps the POCO persistence-ignorant.
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
