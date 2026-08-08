namespace ArtemisBankingPro.Application.Interfaces.Persistence;

public enum IdempotencyStatus
{
    InProgress = 1,
    Completed = 2,
}

/// <summary>
/// Registro de idempotencia para operaciones externamente repetibles
/// (Hermes Pay, transferencias con confirmación, avances de efectivo).
/// </summary>
public sealed class IdempotencyRecord
{
    private IdempotencyRecord() { }

    public IdempotencyRecord(
        string idempotencyKey,
        string actorId,
        string operationType,
        string requestFingerprint,
        DateTimeOffset createdAt
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);

        IdempotencyKey = idempotencyKey;
        ActorId = actorId;
        OperationType = operationType;
        RequestFingerprint = requestFingerprint;
        Status = IdempotencyStatus.InProgress;
        CreatedAt = createdAt;
    }

    public long Id { get; private set; }

    public string IdempotencyKey { get; private set; } = null!;

    public string ActorId { get; private set; } = null!;

    public string OperationType { get; private set; } = null!;

    /// <summary>Huella del payload canónico, para distinguir la misma clave con distinto payload.</summary>
    public string RequestFingerprint { get; private set; } = null!;

    public IdempotencyStatus Status { get; private set; }

    public string? ResultReference { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public void Complete(string resultReference, DateTimeOffset completedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultReference);
        Status = IdempotencyStatus.Completed;
        ResultReference = resultReference;
        CompletedAt = completedAt;
    }
}
