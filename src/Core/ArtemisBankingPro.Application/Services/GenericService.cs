using System.Linq.Expressions;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Domain.Common.Entities;

namespace ArtemisBankingPro.Application.Services;

public sealed class GenericService<T> : IGenericService<T>
    where T : Entity<int> {
    private readonly IGenericRepository<T> _repository;

    public GenericService(IGenericRepository<T> repository) {
        _repository = repository;
    }

    public Task<T?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _repository.GetByIdAsync(id, ct);

    public Task<bool> ExistsAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default
    ) => _repository.ExistsAsync(predicate, ct);

    public Task<int> CountAsync(
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken ct = default
    ) => _repository.CountAsync(predicate, ct);

    public Task<T> AddAsync(T entity, CancellationToken ct = default) =>
        _repository.AddAsync(entity, ct);

    public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default) =>
        _repository.AddRangeAsync(entities, ct);

    public void Update(T entity) => _repository.Update(entity);
}
