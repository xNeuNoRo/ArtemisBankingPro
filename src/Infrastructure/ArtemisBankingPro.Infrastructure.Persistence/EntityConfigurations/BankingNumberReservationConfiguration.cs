using ArtemisBankingPro.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations;

public sealed class BankingNumberReservationConfiguration
    : IEntityTypeConfiguration<BankingNumberReservation> {
    public void Configure(EntityTypeBuilder<BankingNumberReservation> builder) {
        builder.ToTable(
            "BankingNumberReservations",
            table => {
                table.HasCheckConstraint(
                    "CK_BankingNumberReservations_Number_NineDigits",
                    "LEN([Number]) = 9 AND [Number] NOT LIKE '%[^0-9]%'"
                );
                table.HasCheckConstraint(
                    "CK_BankingNumberReservations_ResourceType_Valid",
                     "[ResourceType] IN (1, 2)"
                );
            }
        );

        builder.HasKey(reservation => reservation.Number);
        builder
            .Property(reservation => reservation.Number)
            .HasMaxLength(9)
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql(
                "RIGHT(REPLICATE('0', 9) + CONVERT(varchar(9), NEXT VALUE FOR dbo.BankingNumberSequence), 9)"
            )
            .IsRequired();
        builder.Property(reservation => reservation.ResourceType).IsRequired();
        builder.Property<DateTimeOffset>("CreatedAt").HasDefaultValueSql("GETUTCDATE()");
    }
}
