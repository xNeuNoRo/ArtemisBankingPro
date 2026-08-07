using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations;

public sealed class FinancialOperationConfiguration : IEntityTypeConfiguration<FinancialOperation>
{
    public void Configure(EntityTypeBuilder<FinancialOperation> builder)
    {
        builder.ToTable(
            "FinancialOperations",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_FinancialOperations_Requested_Positive",
                    "[RequestedAmount] > 0"
                );
                table.HasCheckConstraint(
                    "CK_FinancialOperations_Applied_NonNegative",
                    "[AppliedAmount] >= 0"
                );
                table.HasCheckConstraint(
                    "CK_FinancialOperations_Interest_NonNegative",
                    "[InterestAmount] >= 0"
                );
            }
        );
        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.Id).ValueGeneratedNever();

        builder.Property(operation => operation.Kind).IsRequired();
        builder.Property(operation => operation.Status).IsRequired();
        builder
            .Property(operation => operation.RequestedAmount)
            .HasConversion(new MoneyConverter())
            .HasPrecision(18, 2)
            .IsRequired();
        builder
            .Property(operation => operation.AppliedAmount)
            .HasConversion(new MoneyConverter())
            .HasPrecision(18, 2)
            .IsRequired();
        builder
            .Property(operation => operation.InterestAmount)
            .HasConversion(new MoneyConverter())
            .HasPrecision(18, 2)
            .IsRequired();
        builder.Property(operation => operation.InitiatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(operation => operation.OccurredAt).IsRequired();
        builder.Property(operation => operation.RejectionCode).HasMaxLength(60);
        builder.Property(operation => operation.CreditCardId);
        builder
            .Property(operation => operation.LoanNumber)
            .HasConversion(
                loanNumber => loanNumber == null ? null : loanNumber.Value,
                value => value == null ? null : LoanNumber.Create(value).Value
            )
            .HasMaxLength(9);
        builder.Property(operation => operation.MerchantId);

        builder
            .Navigation(operation => operation.AccountTransactions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder
            .Navigation(operation => operation.CardConsumption)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(operation => operation.InitiatedByUserId);
        builder.HasIndex(operation => operation.OccurredAt);
        builder.HasIndex(operation => new { operation.Kind, operation.Status });
        builder.HasIndex(operation => operation.CreditCardId);
        builder.HasIndex(operation => operation.LoanNumber);
        builder.HasIndex(operation => operation.MerchantId);

        builder.Property<DateTimeOffset>("CreatedAt").HasDefaultValueSql("GETUTCDATE()");
    }
}
