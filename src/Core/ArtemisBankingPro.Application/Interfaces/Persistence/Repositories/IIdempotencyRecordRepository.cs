namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

public interface IIdempotencyRecordRepository {
    Task<IdempotencyRecord?> GetAsync(
        string idempotencyKey,
        string actorId,
        CancellationToken ct = default
    );

    Task<IdempotencyRecord> AddAsync(IdempotencyRecord record, CancellationToken ct = default);

    void Update(IdempotencyRecord record);

    /// <summary>
    /// Elimina el registro de idempotencia para permitir reintentar la misma
    /// clave tras un rechazo de negocio (Result.Failure) o un fallo del handler.
    /// </summary>
    void Delete(IdempotencyRecord record);
}
