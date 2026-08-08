using System.Linq.Expressions;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

public class GenericRepository<T> : IGenericRepository<T>
    where T : Entity<int> {
    protected readonly BankingDbContext Context;
    protected readonly DbSet<T> DbSet;

    public GenericRepository(BankingDbContext context) {
        Context = context;
        DbSet = context.Set<T>();
    }

    public virtual Task<T?> GetByIdAsync(int id, CancellationToken ct = default) =>
        DbSet.FirstOrDefaultAsync(entity => entity.Id == id, ct);

    public virtual Task<bool> ExistsAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default
    ) => DbSet.AnyAsync(predicate, ct);

    public virtual Task<int> CountAsync(
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken ct = default
    ) => predicate is null ? DbSet.CountAsync(ct) : DbSet.CountAsync(predicate, ct);

    public virtual async Task<T> AddAsync(T entity, CancellationToken ct = default) {
        await DbSet.AddAsync(entity, ct);
        return entity;
    }

    public virtual Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default) =>
        DbSet.AddRangeAsync(entities, ct);

    public virtual void Update(T entity) => DbSet.Update(entity);
}
