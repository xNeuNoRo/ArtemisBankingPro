using ArtemisBankingPro.Domain.Common.Entities;

namespace ArtemisBankingPro.Application.Interfaces.Services;

/// <summary>
/// Thin maintenance boundary over the generic repository.
/// It does not save changes; the caller owns the unit of work.
/// </summary>
public interface IGenericService<T>
    where T : Entity<int> {
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<bool> ExistsAsync(
        System.Linq.Expressions.Expression<Func<T, bool>> predicate,
        CancellationToken ct = default
    );

    Task<int> CountAsync(
        System.Linq.Expressions.Expression<Func<T, bool>>? predicate = null,
        CancellationToken ct = default
    );

    Task<T> AddAsync(T entity, CancellationToken ct = default);

    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);

    /// <summary>Updates a tracked simple entity; does not attach disconnected graphs.</summary>
    void Update(T entity);
}
