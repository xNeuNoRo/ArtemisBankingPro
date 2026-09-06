using ArtemisBankingPro.Application.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord> {
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder) {
        builder.ToTable("IdempotencyRecords");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).ValueGeneratedOnAdd();

        builder.Property(record => record.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(record => record.ActorId).HasMaxLength(450).IsRequired();
        builder.Property(record => record.OperationType).HasMaxLength(50).IsRequired();
        builder.Property(record => record.RequestFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(record => record.Status).IsRequired();
        builder.Property(record => record.ResultReference).HasMaxLength(100);
        builder.Property(record => record.CreatedAt).IsRequired();
        builder.Property(record => record.CompletedAt);

        // La misma clave de idempotencia solo se puede usar una vez por actor.
        builder.HasIndex(record => new { record.IdempotencyKey, record.ActorId }).IsUnique();
        builder.HasIndex(record => record.OperationType);

        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
