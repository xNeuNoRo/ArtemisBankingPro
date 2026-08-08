using ArtemisBankingPro.Domain.Merchants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations;

public sealed class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        builder.ToTable("Merchants");
        builder.HasKey(merchant => merchant.Id);
        builder.Property(merchant => merchant.Id).ValueGeneratedOnAdd();

        builder.Property(merchant => merchant.Name).HasMaxLength(120).IsRequired();
        builder.Property(merchant => merchant.Description).HasMaxLength(500);
        builder.Property(merchant => merchant.Email).HasMaxLength(320).IsRequired();
        builder.Property(merchant => merchant.PhoneNumber).HasMaxLength(20).IsRequired();
        builder.Property(merchant => merchant.Rnc).HasMaxLength(11).IsRequired();
        builder.Property(merchant => merchant.Status).IsRequired();
        builder.Property(merchant => merchant.AssociatedUserId).HasMaxLength(450);
        builder.Property(merchant => merchant.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(merchant => merchant.CreatedAt).IsRequired();
        builder.Property(merchant => merchant.UpdatedAt);

        // Un comercio por RNC, por correo y un usuario asociado por comercio.
        builder.HasIndex(merchant => merchant.Rnc).IsUnique();
        builder.HasIndex(merchant => merchant.Email).IsUnique();
        builder
            .HasIndex(merchant => merchant.AssociatedUserId)
            .IsUnique()
            .HasFilter("[AssociatedUserId] IS NOT NULL");

        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
