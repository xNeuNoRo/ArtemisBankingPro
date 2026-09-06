namespace ArtemisBankingPro.Domain.Common.Entities;

/// <summary>
/// Representa una entidad de dominio con un identificador único.
/// </summary>
/// <typeparam name="TId"></typeparam>
public abstract class Entity<TId>
    where TId : notnull {
    public TId Id { get; protected set; } = default!;
}
