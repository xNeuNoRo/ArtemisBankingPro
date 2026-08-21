using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace ArtemisBankingPro.Application.Common.Behaviors;

/// <summary>
/// Garantiza que un <see cref="IIdempotentCommand"/> se ejecute a lo sumo una
/// vez por clave y actor (ADR-002):
/// <list type="bullet">
/// <item>La reserva es atómica: la unicidad <c>(IdempotencyKey, ActorId)</c>
/// arbitra las carreras entre solicitudes concurrentes.</item>
/// <item>Los resultados de negocio son terminales (Completed o Rejected) y
/// nunca se eliminan. Una excepción deja InProgress porque no demuestra que
/// un commit financiero haya sido revertido.</item>
/// <item>Una reserva <c>InProgress</c> que supera el lease se considera de
/// resultado desconocido y se marca rechazada, para no duplicar la operación.</item>
/// </list>
/// La clave debe ser estable y suministrada por el caller (header
/// <c>Idempotency-Key</c> en API, nonce de servidor en MVC), nunca derivada
/// del reloj.
/// </summary>
public sealed class IdempotencyBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IIdempotentCommand
    where TResponse : IResult {
    private static readonly TimeSpan InProgressLease = TimeSpan.FromMinutes(10);

    private readonly IIdempotencyRecordRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IBusinessClock _clock;
    private readonly ILogger<IdempotencyBehavior<TRequest, TResponse>> _logger;

    public IdempotencyBehavior(
        IIdempotencyRecordRepository repository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IBusinessClock clock,
        ILogger<IdempotencyBehavior<TRequest, TResponse>> logger
    ) {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask<TResponse> Handle(
        TRequest message,
        MessageHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken
    ) {
        string actorId =
            message.IdempotencyActorId
            ?? _currentUser.UserId
            ?? throw new UnauthenticatedException(
                "Las operaciones idempotentes requieren un actor autenticado."
            );

        string key = message.IdempotencyKey;
        ValidateCallerKey(key);
        string fingerprint = HashFingerprint(message.RequestFingerprint);

        // Comprobamos si ya existe un registro de idempotencia para esta clave y actor:
        IdempotencyRecord? existing = await _repository.GetAsync(
            key,
            actorId,
            cancellationToken
        );
        if (existing is not null) {
            await HandleExistingAsync(
                existing,
                typeof(TRequest).Name,
                fingerprint
            );
        }

        // Reservamos la clave de idempotencia para este actor y operación
        IdempotencyRecord record = new(
            key,
            actorId,
            typeof(TRequest).Name,
            fingerprint,
            _clock.NowUtc
        );
        Result claim = await ClaimAsync(record, cancellationToken);
        if (claim.IsFailure) {
            IdempotencyRecord? winner = await _repository.GetAsync(
                key,
                actorId,
                cancellationToken
            );
            await HandleExistingAsync(winner ?? record, typeof(TRequest).Name, fingerprint);
        }

        // Ejecutamos el handler y finalizamos el registro de idempotencia según el resultado:
        TResponse response;
        try {
            response = await next(message, cancellationToken);
        }
        catch (OperationCanceledException ex) {
            // La cancelación no prueba que la transacción financiera haya sido
            // revertida. Mantener InProgress fuerza el tratamiento seguro de
            // resultado desconocido cuando expire el lease.
            _logger.LogWarning(
                ex,
                "La operación idempotente {OperationType} fue cancelada con resultado desconocido (clave {KeyHash}).",
                typeof(TRequest).Name,
                HashFingerprint(key)[..12]
            );
            throw new OperationCanceledException(
                "La operación idempotente fue cancelada y su resultado quedó desconocido.",
                ex,
                ex.CancellationToken
            );
        }
        // El resto de excepciones se propaga y deja el registro InProgress, para que
        // el lease lo recupere y marque como Rejected si no se confirma la operación
        if (response.IsSuccess) {
            string reference = response.ResultReference ?? key;
            await FinalizeAsync(record, rejected: false, reference: reference);
        }
        else {
            string reference = response.Error?.Code ?? "Rejected";
            await FinalizeAsync(record, rejected: true, reference: reference);
        }

        return response;
    }

    private async Task HandleExistingAsync(
        IdempotencyRecord existing,
        string operationType,
        string fingerprint
    ) {
        if (!string.Equals(existing.OperationType, operationType, StringComparison.Ordinal)) {
            throw new IdempotencyConflictException(
                "La clave de idempotencia ya fue utilizada para otro tipo de operación."
            );
        }

        if (existing.RequestFingerprint != fingerprint) {
            throw new IdempotencyConflictException(
                "La clave de idempotencia fue reutilizada con un payload distinto."
            );
        }

        switch (existing.Status) {
            case IdempotencyStatus.Completed:
                throw new IdempotencyConflictException(
                    "La operación ya fue procesada con esta clave de idempotencia.",
                    existing.ResultReference
                );
            case IdempotencyStatus.Rejected:
                throw new IdempotencyConflictException(
                    "La operación ya fue rechazada con esta clave de idempotencia.",
                    existing.ResultReference
                );
            case IdempotencyStatus.InProgress:
                if (existing.CreatedAt.Add(InProgressLease) <= _clock.NowUtc) {
                    // Lease expirado: resultado desconocido. Se marca terminal
                    // para no duplicar la operación; el caller debe reintentar
                    // con una nueva clave.
                    await FinalizeAsync(
                        existing,
                        rejected: true,
                        reference: "ExpiredUnknownOutcome"
                    );
                    throw new IdempotencyConflictException(
                        "La operación quedó en estado desconocido (lease expirado); reintente con una nueva clave."
                    );
                }

                throw new IdempotencyConflictException(
                    "La operación ya está en proceso con la misma clave de idempotencia."
                );
            default:
                throw new IdempotencyConflictException(
                    "Estado de idempotencia no reconocido."
                );
        }
    }

    private static void ValidateCallerKey(string key) {
        if (string.IsNullOrWhiteSpace(key)) {
            throw new MissingIdempotencyKeyException();
        }

        if (key.Length > 128) {
            throw new ValidationException(
                [
                    new ValidationFailure(
                        nameof(IIdempotentCommand.IdempotencyKey),
                        "La clave de idempotencia es obligatoria y no debe exceder 128 caracteres."
                    ),
                ]
            );
        }
    }

    private static string HashFingerprint(string fingerprint) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))
            .ToLowerInvariant();

    private async Task<Result> ClaimAsync(IdempotencyRecord record, CancellationToken ct) =>
        await _unitOfWork.ExecuteInTransactionAsync(
            async token => {
                await _repository.AddAsync(record, token);
                return Result.Success();
            },
            ct: ct
        );

    private async Task FinalizeAsync(
        IdempotencyRecord record,
        bool rejected,
        string reference
    ) {
        if (rejected) {
            record.Reject(reference, _clock.NowUtc);
        }
        else {
            record.Complete(reference, _clock.NowUtc);
        }

        Result result = await _unitOfWork.ExecuteInTransactionAsync(
            _ => {
                _repository.Update(record);
                return Task.FromResult(Result.Success());
            },
            ct: CancellationToken.None
        );

        // Best-effort: la operación financiera ya se confirmó; un fallo aquí no
        // debe revertirla ni propagarse. Se deja el registro InProgress y el
        // lease lo recupera.
        if (result.IsFailure) {
            _logger.LogWarning(
                "No se pudo finalizar el registro de idempotencia {OperationType} ({ErrorCode}, clave {KeyHash}).",
                record.OperationType,
                result.Error!.Code,
                HashFingerprint(record.IdempotencyKey)[..12]
            );
        }
    }
}
