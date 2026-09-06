using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations;

public sealed class AccountTransactionConfiguration : IEntityTypeConfiguration<AccountTransaction> {
    public void Configure(EntityTypeBuilder<AccountTransaction> builder) {
        builder.ToTable(
            "AccountTransactions",
            table => {
                table.HasCheckConstraint("CK_AccountTransactions_Amount_Positive", "[Amount] > 0");
                table.HasCheckConstraint(
                    "CK_AccountTransactions_Direction_Valid",
                    "[Direction] IN (1, 2)"
                );
            }
        );
        builder.HasKey(transaction => transaction.Id);
        builder.Property(transaction => transaction.Id).ValueGeneratedOnAdd();

        builder.Property(transaction => transaction.FinancialOperationId).IsRequired();
        builder
            .Property(transaction => transaction.AccountNumber)
            .HasConversion(new AccountNumberConverter())
            .HasMaxLength(9)
            .IsRequired();
        builder.Property(transaction => transaction.Direction).IsRequired();
        builder
            .Property(transaction => transaction.Amount)
            .HasConversion(new MoneyConverter())
            .HasPrecision(18, 2)
            .IsRequired();
        builder.Property(transaction => transaction.OriginReference).HasMaxLength(100).IsRequired();
        builder
            .Property(transaction => transaction.BeneficiaryReference)
            .HasMaxLength(100)
            .IsRequired();

        builder
            .HasOne<FinancialOperation>()
            .WithMany(operation => operation.AccountTransactions)
            .HasForeignKey(transaction => transaction.FinancialOperationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Historial por cuenta y correlación por operación.
        builder.HasIndex(transaction => transaction.AccountNumber);
        builder.HasIndex(transaction => transaction.FinancialOperationId);

        builder.Property<DateTimeOffset>("CreatedAt").HasDefaultValueSql("GETUTCDATE()");
    }
}
