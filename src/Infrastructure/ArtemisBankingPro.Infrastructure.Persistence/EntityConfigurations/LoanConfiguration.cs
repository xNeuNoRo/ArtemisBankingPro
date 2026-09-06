using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations;

public sealed class LoanConfiguration : IEntityTypeConfiguration<Loan> {
    public void Configure(EntityTypeBuilder<Loan> builder) {
        builder.ToTable(
            "Loans",
            table => {
                table.HasCheckConstraint("CK_Loans_Principal_Positive", "[ApprovedPrincipal] > 0");
                table.HasCheckConstraint(
                    "CK_Loans_Term_Allowed",
                    "[TermMonths] IN (6, 12, 18, 24, 30, 36, 42, 48, 54, 60)"
                );
                table.HasCheckConstraint(
                    "CK_Loans_Number_NineDigits",
                    "LEN([Number]) = 9 AND [Number] NOT LIKE '%[^0-9]%'"
                );
            }
        );
        builder.HasKey(loan => loan.Id);
        builder.Property(loan => loan.Id).ValueGeneratedOnAdd();

        builder.Property(loan => loan.CustomerUserId).HasMaxLength(450).IsRequired();
        builder
            .Property(loan => loan.Number)
            .HasConversion(new LoanNumberConverter())
            .HasMaxLength(9)
            .IsRequired();
        builder
            .Property(loan => loan.ApprovedPrincipal)
            .HasConversion(new MoneyConverter())
            .HasPrecision(18, 2)
            .IsRequired();
        builder.Property(loan => loan.TermMonths).IsRequired();
        builder
            .Property(loan => loan.AnnualInterestRate)
            .HasConversion(new InterestRateConverter())
            .HasPrecision(10, 6)
            .IsRequired();
        builder.Property(loan => loan.Status).IsRequired();
        builder.Property(loan => loan.AssignedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(loan => loan.IssuedAt).IsRequired();
        builder.Property(loan => loan.CompletedAt);

        builder.Ignore(loan => loan.OutstandingAmount);
        builder.Ignore(loan => loan.IsDelinquent);

        builder
            .Navigation(loan => loan.Installments)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(loan => loan.Number).IsUnique();

        // Un préstamo activo por cliente.
        builder.HasIndex(loan => loan.CustomerUserId).IsUnique().HasFilter("[Status] = 1");

        builder.Property<byte[]>("RowVersion").IsRowVersion();
        builder.Property<DateTimeOffset>("CreatedAt").HasDefaultValueSql("GETUTCDATE()");
        builder.Property<DateTimeOffset?>("UpdatedAt");
    }
}
