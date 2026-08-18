using System.Security.Cryptography;
using System.Text;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Services;

/// <summary>
/// Emite y valida nonces de confirmación single-use. Solo se persiste el hash
/// SHA-256 del nonce; el valor en claro se entrega una única vez al emitirlo.
/// El consumo es atómico: la lectura-verificación-escritura se ejecuta en una
/// transacción con control de concurrencia optimista (rowversion) para que un
/// solo consumidor gane la carrera (un doble clic o un reintento no consume
/// dos veces).
/// </summary>
public sealed class ConfirmationTokenService : IConfirmationTokenService {
    private const string AlreadyConsumedCode = "ConfirmationToken.AlreadyConsumed";
    private const string ExpiredCode = "ConfirmationToken.Expired";

    private readonly IConfirmationTokenRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;

    public ConfirmationTokenService(
        IConfirmationTokenRepository repository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock
    ) {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<string> IssueAsync(
        string actorId,
        string operationType,
        string requestFingerprint,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeToLive, TimeSpan.Zero);

        string nonce = CreateRawToken();
        var token = new ConfirmationToken(
            ComputeHash(nonce),
            actorId,
            operationType,
            requestFingerprint,
            _clock.NowUtc.Add(timeToLive),
            _clock.NowUtc
        );

        Result result = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                await _repository.AddAsync(token, ct);
                return Result.Success();
            },
            ct: cancellationToken
        );

        if (result.IsFailure) {
            throw new InvalidOperationException(
                "No fue posible registrar la confirmación de la operación."
            );
        }

        return nonce;
    }

    public async Task<ConfirmationValidationResult> ValidateAndConsumeAsync(
        string token,
        string actorId,
        string operationType,
        string requestFingerprint,
        CancellationToken cancellationToken = default
    ) {
        if (string.IsNullOrWhiteSpace(token)) {
            return ConfirmationValidationResult.Invalid(
                ConfirmationTokenInvalidReason.NotFound
            );
        }

        string tokenHash = ComputeHash(token);
        ConfirmationToken? existing = await _repository.GetByTokenHashAsync(
            tokenHash,
            cancellationToken
        );

        if (existing is null) {
            return ConfirmationValidationResult.Invalid(
                ConfirmationTokenInvalidReason.NotFound
            );
        }

        ConfirmationValidationResult? staticCheck = StaticValidation(
            existing,
            actorId,
            operationType,
            requestFingerprint
        );
        if (staticCheck is not null) {
            return staticCheck;
        }

        Result consume = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                ConfirmationToken? fresh = await _repository.GetByTokenHashAsync(
                    tokenHash,
                    ct
                );
                if (fresh is null) {
                    return Result.Failure(
                        DomainError.Conflict(AlreadyConsumedCode, "confirmación no disponible")
                    );
                }

                if (fresh.IsExpired(_clock.NowUtc)) {
                    return Result.Failure(
                        DomainError.Conflict(ExpiredCode, "confirmación expirada")
                    );
                }

                if (fresh.IsConsumed) {
                    return Result.Failure(
                        DomainError.Conflict(AlreadyConsumedCode, "confirmación ya utilizada")
                    );
                }

                fresh.Consume(_clock.NowUtc);
                return Result.Success();
            },
            // ReadCommitted + rowversion: el segundo consumidor concurrente
            // pierde con DbUpdateConcurrencyException (traducida por UnitOfWork
            // a Concurrency.Conflict), sin deadlocks de range locks.
            ct: cancellationToken
        );

        if (consume.IsFailure) {
            return consume.Error!.Code == ExpiredCode
                ? ConfirmationValidationResult.Invalid(ConfirmationTokenInvalidReason.Expired)
                : ConfirmationValidationResult.Invalid(
                    ConfirmationTokenInvalidReason.AlreadyConsumed
                );
        }

        return ConfirmationValidationResult.Valid();
    }

    private ConfirmationValidationResult? StaticValidation(
        ConfirmationToken token,
        string actorId,
        string operationType,
        string requestFingerprint
    ) {
        if (token.ActorId != actorId) {
            return ConfirmationValidationResult.Invalid(
                ConfirmationTokenInvalidReason.ActorMismatch
            );
        }

        if (token.OperationType != operationType) {
            return ConfirmationValidationResult.Invalid(
                ConfirmationTokenInvalidReason.OperationMismatch
            );
        }

        if (token.RequestFingerprint != requestFingerprint) {
            return ConfirmationValidationResult.Invalid(
                ConfirmationTokenInvalidReason.FingerprintMismatch
            );
        }

        if (token.IsConsumed) {
            return ConfirmationValidationResult.Invalid(
                ConfirmationTokenInvalidReason.AlreadyConsumed
            );
        }

        if (token.IsExpired(_clock.NowUtc)) {
            return ConfirmationValidationResult.Invalid(
                ConfirmationTokenInvalidReason.Expired
            );
        }

        return null;
    }

    /// <summary>Nonce criptográficamente seguro de 32 bytes en Base64Url sin padding.</summary>
    private static string CreateRawToken() {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Hash SHA-256 del nonce en claro. El nonce ya posee alta entropía
    /// (256 bits), por lo que el hash sin pepper es suficiente; nunca se
    /// persiste el nonce en claro.
    /// </summary>
    private static string ComputeHash(string rawToken) {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
