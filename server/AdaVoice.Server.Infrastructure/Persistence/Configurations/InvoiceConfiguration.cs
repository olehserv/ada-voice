using AdaVoice.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdaVoice.Server.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.Property(x => x.Id).HasValueGenerator<UuidV7ValueGenerator>().ValueGeneratedOnAdd();

        builder.Property(x => x.Status).HasConversion(StatusConverters.InvoiceStatus);
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_invoices_status", "status IN ('draft', 'issued', 'paid', 'overdue', 'cancelled', 'refunded')"));

        builder.Property(x => x.AmountUah).HasColumnType("numeric(12,2)");

        builder.HasIndex(x => x.Number).IsUnique();

        // Overdue/reminder jobs scan issued invoices past due.
        builder.HasIndex(x => new { x.Status, x.DueAt });
    }
}
