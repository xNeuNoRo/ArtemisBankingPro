using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations;

public sealed class CardConsumptionConfiguration : IEntityTypeConfiguration<CardConsumption>
{
    public void Configure(EntityTypeBuilder<CardConsumption> builder)
    {
        builder.ToTable(
            "CardConsumptions",
            table =>
            {
                table.HasCheckConstraint("CK_CardConsumptions_Amount_Positive", "[Amount] > 0");
            }
        );
        builder.HasKey(consumption => consumption.Id);
        builder.Property(consumption => consumption.Id).ValueGeneratedOnAdd();

        builder.Property(consumption => consumption.FinancialOperationId).IsRequired();
        builder.Property(consumption => consumption.CreditCardId).IsRequired();
        builder.Property(consumption => consumption.MerchantId);
        builder
            .Property(consumption => consumption.MerchantDisplayName)
            .HasMaxLength(120)
            .IsRequired();
        builder.Property(consumption => consumption.Type).IsRequired();
        builder
            .Property(consumption => consumption.Amount)
            .HasConversion(new MoneyConverter())
            .HasPrecision(18, 2)
            .IsRequired();

        builder
            .HasOne<FinancialOperation>()
            .WithOne(operation => operation.CardConsumption)
            .HasForeignKey<CardConsumption>(consumption => consumption.FinancialOperationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne<CreditCard>()
            .WithMany()
            .HasForeignKey(consumption => consumption.CreditCardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(consumption => consumption.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(consumption => consumption.CreditCardId);
        builder.HasIndex(consumption => consumption.MerchantId);

        builder.Property<DateTimeOffset>("CreatedAt").HasDefaultValueSql("GETUTCDATE()");
    }
}
