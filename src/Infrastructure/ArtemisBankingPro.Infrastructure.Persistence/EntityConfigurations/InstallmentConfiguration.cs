using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations;

public sealed class InstallmentConfiguration : IEntityTypeConfiguration<Installment> {
    public void Configure(EntityTypeBuilder<Installment> builder) {
        builder.ToTable(
            "Installments",
            table => {
                table.HasCheckConstraint(
                    "CK_Installments_Scheduled_NonNegative",
                    "[ScheduledAmount] >= 0"
                );
                table.HasCheckConstraint("CK_Installments_Paid_NonNegative", "[PaidAmount] >= 0");
                table.HasCheckConstraint(
                    "CK_Installments_Paid_Within_Scheduled",
                    "[PaidAmount] <= [ScheduledAmount]"
                );
            }
        );
        builder.HasKey(installment => installment.Id);
        builder.Property(installment => installment.Id).ValueGeneratedOnAdd();

        builder.Property(installment => installment.LoanId).IsRequired();
        builder.Property(installment => installment.Number).IsRequired();
        builder.Property(installment => installment.DueDate).IsRequired();
        builder
            .Property(installment => installment.ScheduledAmount)
            .HasConversion(new MoneyConverter())
            .HasPrecision(18, 2)
            .IsRequired();
        builder
            .Property(installment => installment.InterestAmount)
            .HasConversion(new MoneyConverter())
            .HasPrecision(18, 2)
            .IsRequired();
        builder
            .Property(installment => installment.PrincipalAmount)
            .HasConversion(new MoneyConverter())
            .HasPrecision(18, 2)
            .IsRequired();
        builder
            .Property(installment => installment.PaidAmount)
            .HasConversion(new MoneyConverter())
            .HasPrecision(18, 2)
            .IsRequired();
        builder.Property(installment => installment.IsOverdue).IsRequired();

        builder.Ignore(installment => installment.Status);
        builder.Ignore(installment => installment.RemainingAmount);

        builder
            .HasOne<Loan>()
            .WithMany(loan => loan.Installments)
            .HasForeignKey(installment => installment.LoanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(installment => new { installment.LoanId, installment.Number }).IsUnique();
        builder.HasIndex(installment => new { installment.LoanId, installment.DueDate });

        builder.Property<byte[]>("RowVersion").IsRowVersion();
        builder.Property<DateTimeOffset>("CreatedAt").HasDefaultValueSql("GETUTCDATE()");
        builder.Property<DateTimeOffset?>("UpdatedAt");
    }
}
