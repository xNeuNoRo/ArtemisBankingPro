using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;
using ArtemisBankingPro.Domain.Accounts.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations;

public sealed class BeneficiaryConfiguration : IEntityTypeConfiguration<Beneficiary> {
    public void Configure(EntityTypeBuilder<Beneficiary> builder) {
        builder.ToTable("Beneficiaries");
        builder.HasKey(beneficiary => beneficiary.Id);
        builder.Property(beneficiary => beneficiary.Id).ValueGeneratedOnAdd();

        builder.Property(beneficiary => beneficiary.OwnerUserId).HasMaxLength(450).IsRequired();
        builder.Property(beneficiary => beneficiary.DestinationAccountId).IsRequired();
        builder.Property(beneficiary => beneficiary.CreatedAt).IsRequired();

        builder
            .HasOne<SavingsAccount>()
            .WithMany()
            .HasForeignKey(beneficiary => beneficiary.DestinationAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(beneficiary => beneficiary.OwnerUserId);
        builder
            .HasIndex(beneficiary => new {
                beneficiary.OwnerUserId,
                beneficiary.DestinationAccountId,
            })
            .IsUnique();
    }
}
