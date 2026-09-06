using ArtemisBankingPro.Application.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations;

public sealed class ConfirmationTokenConfiguration : IEntityTypeConfiguration<ConfirmationToken> {
    public void Configure(EntityTypeBuilder<ConfirmationToken> builder) {
        builder.ToTable("ConfirmationTokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedOnAdd();

        builder.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(token => token.ActorId).HasMaxLength(450).IsRequired();
        builder.Property(token => token.OperationType).HasMaxLength(50).IsRequired();
        builder.Property(token => token.RequestFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(token => token.ExpiresAtUtc).IsRequired();
        builder.Property(token => token.CreatedAtUtc).IsRequired();
        builder.Property(token => token.ConsumedAtUtc);

        // Un nonce se identifica por su hash: único en el sistema.
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => new { token.ActorId, token.OperationType });

        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
