namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

public interface IIdempotencyRecordRepository {
    Task<IdempotencyRecord?> GetAsync(
        string idempotencyKey,
        string actorId,
        CancellationToken ct = default
    );

    Task<IdempotencyRecord> AddAsync(IdempotencyRecord record, CancellationToken ct = default);

    void Update(IdempotencyRecord record);
}
