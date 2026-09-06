namespace ArtemisBankingPro.Application.Interfaces.Persistence;

/// <summary>
/// Nonce de confirmación emitido por el servidor para operaciones
/// transaccionales de la WebApp (transferencias, pagos, avances, depósitos,
/// retiros, etc.).
///
/// Solo se persiste el hash del nonce (<see cref="TokenHash"/>, SHA-256 del
/// valor en claro), nunca el nonce mismo. Se vincula al actor, al tipo de
/// operación y a la huella del payload, expira y se consume una sola vez.
/// </summary>
public sealed class ConfirmationToken {
    private ConfirmationToken() { }

    public ConfirmationToken(
        string tokenHash,
        string actorId,
        string operationType,
        string requestFingerprint,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);

        TokenHash = tokenHash;
        ActorId = actorId;
        OperationType = operationType;
        RequestFingerprint = requestFingerprint;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    public long Id { get; }

    /// <summary>Hash SHA-256 del nonce en claro. Nunca se persiste el nonce.</summary>
    public string TokenHash { get; private set; } = null!;

    public string ActorId { get; private set; } = null!;

    public string OperationType { get; private set; } = null!;

    /// <summary>Huella del payload canónico, para vincular el nonce a la operación concreta.</summary>
    public string RequestFingerprint { get; private set; } = null!;

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public bool IsConsumed => ConsumedAtUtc is not null;

    public bool IsExpired(DateTimeOffset now) => ExpiresAtUtc <= now;

    /// <summary>
    /// Marca el nonce como utilizado. Lanza si ya fue consumido; la invocación
    /// se hace dentro de una transacción con aislamiento apropiado para que un
    /// solo consumidor gane la carrera.
    /// </summary>
    public void Consume(DateTimeOffset at) {
        if (IsConsumed) {
            throw new InvalidOperationException("La confirmación ya fue utilizada.");
        }

        ConsumedAtUtc = at;
    }
}
