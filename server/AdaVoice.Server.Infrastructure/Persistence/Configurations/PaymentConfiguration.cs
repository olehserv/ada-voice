using AdaVoice.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdaVoice.Server.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(x => x.Id).HasValueGenerator<UuidV7ValueGenerator>().ValueGeneratedOnAdd();

        builder.Property(x => x.Provider).HasConversion(StatusConverters.PaymentProvider);
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_payments_provider", "provider IN ('manual_bank_transfer', 'liqpay', 'wayforpay', 'fondy')"));

        builder.Property(x => x.AmountUah).HasColumnType("numeric(12,2)");

        // Webhook idempotency: unique per provider transaction, only when a tx id exists
        // (manual payments have none).
        builder.HasIndex(x => new { x.Provider, x.ProviderTxId })
            .IsUnique()
            .HasFilter("provider_tx_id IS NOT NULL");

        // FK → invoices (required). No-navigation overload; conservative Restrict.
        builder.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK → users, nullable: marked_by_user_id is set only for manual "mark paid".
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.MarkedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
