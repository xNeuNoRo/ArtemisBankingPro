using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations;

public sealed class CreditCardConfiguration : IEntityTypeConfiguration<CreditCard>
{
    public void Configure(EntityTypeBuilder<CreditCard> builder)
    {
        builder.ToTable(
            "CreditCards",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_CreditCards_LastFour_Digits",
                    "[LastFour] LIKE '[0-9][0-9][0-9][0-9]'"
                );
                table.HasCheckConstraint(
                    "CK_CreditCards_Fingerprint_Hex",
                    "LEN([PanFingerprint]) = 64 AND [PanFingerprint] NOT LIKE '%[^0-9A-Fa-f]%'"
                );
                table.HasCheckConstraint("CK_CreditCards_Debt_NonNegative", "[CurrentDebt] >= 0");
                table.HasCheckConstraint("CK_CreditCards_Limit_Positive", "[CreditLimit] > 0");
                table.HasCheckConstraint(
                    "CK_CreditCards_Debt_Within_Limit",
                    "[CurrentDebt] <= [CreditLimit]"
                );
            }
        );
        builder.HasKey(card => card.Id);
        builder.Property(card => card.Id).ValueGeneratedOnAdd();

        builder.Property(card => card.CustomerUserId).HasMaxLength(450).IsRequired();
        builder.Property(card => card.LastFour).HasMaxLength(4).IsRequired();
        builder.Property(card => card.PanFingerprint).HasMaxLength(64).IsRequired();
        builder
            .Property(card => card.CvcDigest)
            .HasConversion(new CvcDigestConverter())
            .HasMaxLength(64)
            .IsRequired();
        builder
            .Property(card => card.CreditLimit)
            .HasConversion(new MoneyConverter())
            .HasPrecision(18, 2)
            .IsRequired();
        builder
            .Property(card => card.CurrentDebt)
            .HasConversion(new MoneyConverter())
            .HasPrecision(18, 2)
            .IsRequired();
        builder.Property(card => card.Status).IsRequired();
        builder.Property(card => card.AssignedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(card => card.IssuedAt).IsRequired();
        builder.Property(card => card.CancelledAt);

        builder.OwnsOne(
            card => card.Expiration,
            expiration =>
            {
                expiration
                    .Property(expirationValue => expirationValue.Month)
                    .HasColumnName("ExpirationMonth")
                    .IsRequired();
                expiration
                    .Property(expirationValue => expirationValue.Year)
                    .HasColumnName("ExpirationYear")
                    .IsRequired();
            }
        );

        builder.Ignore(card => card.AvailableCredit);

        // La huella del PAN es la clave de búsqueda única de la tarjeta.
        builder.HasIndex(card => card.PanFingerprint).IsUnique();
        builder.HasIndex(card => card.CustomerUserId);

        builder.Property<byte[]>("RowVersion").IsRowVersion();
        builder.Property<DateTimeOffset>("CreatedAt").HasDefaultValueSql("GETUTCDATE()");
        builder.Property<DateTimeOffset?>("UpdatedAt");
    }
}
