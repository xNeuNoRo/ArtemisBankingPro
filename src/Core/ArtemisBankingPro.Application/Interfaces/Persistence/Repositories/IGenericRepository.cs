using System.Linq.Expressions;
using ArtemisBankingPro.Domain.Common.Entities;

namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

/// <summary>
/// Repositorio genérico con operaciones CRUD de mantenimiento.
/// </summary>
public interface IGenericRepository<T>
    where T : Entity<int> {
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Recarga el agregado tracked desde la base de datos antes de una mutación.
    /// No expone consultas arbitrarias ni modifica el grafo de relaciones.
    /// </summary>
    Task ReloadAsync(T entity, CancellationToken ct = default);

    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task<int> CountAsync(
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken ct = default
    );

    Task<T> AddAsync(T entity, CancellationToken ct = default);

    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);

    /// <summary>
    /// Marks no state by itself; the entity must already be tracked and the
    /// caller owns the unit of work. Detached graphs are rejected.
    /// </summary>
    void Update(T entity);
}
