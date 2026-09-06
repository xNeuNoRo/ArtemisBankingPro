using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

public sealed class IdempotencyRecordRepository : IIdempotencyRecordRepository {
    private readonly BankingDbContext _context;

    public IdempotencyRecordRepository(BankingDbContext context) {
        _context = context;
    }

    public Task<IdempotencyRecord?> GetAsync(
        string idempotencyKey,
        string actorId,
        CancellationToken ct = default
    ) =>
        _context
            .Set<IdempotencyRecord>()
            .FirstOrDefaultAsync(
                record => record.IdempotencyKey == idempotencyKey && record.ActorId == actorId,
                ct
            );

    public async Task<IdempotencyRecord> AddAsync(
        IdempotencyRecord record,
        CancellationToken ct = default
    ) {
        await _context.Set<IdempotencyRecord>().AddAsync(record, ct);
        return record;
    }

    public void Update(IdempotencyRecord record) {
        EntityEntry<IdempotencyRecord> entry = _context.Entry(record);
        if (entry.State == EntityState.Detached) {
            _context.Attach(record);
            entry.State = EntityState.Modified;
        }
    }

    public void Delete(IdempotencyRecord record) =>
        _context.Set<IdempotencyRecord>().Remove(record);
}
