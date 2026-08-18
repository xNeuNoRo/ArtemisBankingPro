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
/// <item>El registro es terminal (Completed o Rejected) y nunca se elimina:
/// repetir la misma clave devuelve un resultado determinista sin re-ejecutar
/// ni duplicar la operación.</item>
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

        // 1. Ruta rápida: registro ya existente (terminal, en proceso o expirado).
        IdempotencyRecord? existing = await _repository.GetAsync(
            key,
            actorId,
            cancellationToken
        );
        if (existing is not null) {
            await HandleExistingAsync(existing, fingerprint, cancellationToken);
        }

        // 2. Reserva atómica: la unicidad (key, actor) arbitra las carreras.
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
            await HandleExistingAsync(winner ?? record, fingerprint, cancellationToken);
        }

        // 3. Ejecutar el caso de uso.
        TResponse response;
        try {
            response = await next(message, cancellationToken);
        }
        catch {
            await FinalizeAsync(record, rejected: true, reference: "HandlerFailed", ct: cancellationToken);
            throw;
        }

        // 4. Estado terminal (nunca se elimina).
        if (response.IsSuccess) {
            string reference = response.ResultReference ?? key;
            await FinalizeAsync(record, rejected: false, reference: reference, ct: cancellationToken);
        }
        else {
            string reference = response.Error?.Code ?? "Rejected";
            await FinalizeAsync(record, rejected: true, reference: reference, ct: cancellationToken);
        }

        return response;
    }

    private async Task HandleExistingAsync(
        IdempotencyRecord existing,
        string fingerprint,
        CancellationToken cancellationToken
    ) {
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
                        reference: "ExpiredUnknownOutcome",
                        ct: cancellationToken
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
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128) {
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
        string reference,
        CancellationToken ct
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
            ct: ct
        );

        // Best-effort: la operación financiera ya se confirmó; un fallo aquí no
        // debe revertirla ni propagarse. Se deja el registro InProgress y el
        // lease lo recupera.
        if (result.IsFailure) {
            _logger.LogWarning(
                "No se pudo finalizar el registro de idempotencia {Key} ({ErrorCode}).",
                record.IdempotencyKey,
                result.Error!.Code
            );
        }
    }
}
