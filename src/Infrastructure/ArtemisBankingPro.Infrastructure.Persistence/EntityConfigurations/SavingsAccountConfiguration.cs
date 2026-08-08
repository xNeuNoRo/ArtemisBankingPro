using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations;

public sealed class SavingsAccountConfiguration : IEntityTypeConfiguration<SavingsAccount> {
    public void Configure(EntityTypeBuilder<SavingsAccount> builder) {
        builder.ToTable(
            "SavingsAccounts",
            table => {
                table.HasCheckConstraint(
                    "CK_SavingsAccounts_Balance_NonNegative",
                    "[Balance] >= 0"
                );
                table.HasCheckConstraint(
                    "CK_SavingsAccounts_Number_NineDigits",
                    "LEN([Number]) = 9 AND [Number] NOT LIKE '%[^0-9]%'"
                );
            }
        );
        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).ValueGeneratedOnAdd();

        builder.Property(account => account.OwnerUserId).HasMaxLength(450).IsRequired();
        builder
            .Property(account => account.Number)
            .HasConversion(new AccountNumberConverter())
            .HasMaxLength(9)
            .IsRequired();
        builder.Property(account => account.Type).IsRequired();
        builder.Property(account => account.Status).IsRequired();
        builder
            .Property(account => account.Balance)
            .HasConversion(new MoneyConverter())
            .HasPrecision(18, 2)
            .IsRequired();
        builder.Property(account => account.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(account => account.OpenedAt).IsRequired();
        builder.Property(account => account.CancelledAt);

        // Número único; índice compuesto para "una cuenta principal por cliente".
        builder.HasIndex(account => account.Number).IsUnique();
        builder.HasIndex(account => account.OwnerUserId);
        builder
            .HasIndex(account => new { account.OwnerUserId, account.Type })
            .IsUnique()
            .HasFilter("[Type] = 1");

        // Concurrencia optimista y auditoría.
        builder.Property<byte[]>("RowVersion").IsRowVersion();
        builder.Property<DateTimeOffset>("CreatedAt").HasDefaultValueSql("GETUTCDATE()");
        builder.Property<DateTimeOffset?>("UpdatedAt");
    }
}
