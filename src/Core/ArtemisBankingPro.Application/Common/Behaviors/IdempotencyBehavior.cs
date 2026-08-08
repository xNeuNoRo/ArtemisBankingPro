using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Common.Behaviors;

/// <summary>
/// Reserva una key de idempotencia antes de ejecutar un Command que la
/// implemente y la completa al terminar.
/// </summary>
public sealed class IdempotencyBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IIdempotentCommand
{
    private readonly IIdempotencyRecordRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IBusinessClock _clock;

    public IdempotencyBehavior(
        IIdempotencyRecordRepository repository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IBusinessClock clock
    )
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async ValueTask<TResponse> Handle(
        TRequest message,
        MessageHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken
    )
    {
        string actorId =
            _currentUser.UserId
            ?? throw new UnauthenticatedException(
                "Las operaciones idempotentes requieren un actor autenticado."
            );

        var existing = await _repository.GetAsync(
            message.IdempotencyKey,
            actorId,
            cancellationToken
        );

        if (existing is not null)
        {
            if (existing.RequestFingerprint != message.RequestFingerprint)
            {
                throw new IdempotencyConflictException(
                    "La clave de idempotencia fue reutilizada con un payload distinto."
                );
            }

            if (existing.Status == IdempotencyStatus.InProgress)
            {
                throw new IdempotencyConflictException(
                    "La operación ya está en proceso con la misma clave de idempotencia."
                );
            }

            throw new IdempotencyConflictException(
                "La operación ya fue procesada con esta clave de idempotencia."
            );
        }

        var record = new IdempotencyRecord(
            message.IdempotencyKey,
            actorId,
            message.GetType().Name,
            message.RequestFingerprint,
            _clock.NowUtc
        );
        await _repository.AddAsync(record, cancellationToken);

        TResponse response;
        try
        {
            response = await next(message, cancellationToken);
        }
        catch
        {
            await DeleteRecordAsync(record, cancellationToken);
            throw;
        }

        if (IsBusinessFailure(response))
        {
            await DeleteRecordAsync(record, cancellationToken);
            return response;
        }

        record.Complete(message.IdempotencyKey, _clock.NowUtc);
        await SaveRecordAsync(record, cancellationToken);

        return response;
    }

    private async Task DeleteRecordAsync(
        IdempotencyRecord record,
        CancellationToken cancellationToken
    )
    {
        await _unitOfWork.ExecuteInTransactionAsync(
            _ =>
            {
                _repository.Delete(record);
                return Task.FromResult(Result.Success());
            },
            ct: cancellationToken
        );
    }

    private async Task SaveRecordAsync(
        IdempotencyRecord record,
        CancellationToken cancellationToken
    )
    {
        await _unitOfWork.ExecuteInTransactionAsync(
            _ =>
            {
                _repository.Update(record);
                return Task.FromResult(Result.Success());
            },
            ct: cancellationToken
        );
    }

    private static bool IsBusinessFailure(TResponse response)
    {
        if (response is Result result)
        {
            return !result.IsSuccess;
        }

        Type responseType = typeof(TResponse);
        if (
            responseType.IsGenericType
            && responseType.GetGenericTypeDefinition() == typeof(Result<>)
        )
        {
            return responseType.GetProperty(nameof(Result.IsSuccess)) is { } isSuccessProperty
                && isSuccessProperty.GetValue(response) is false;
        }

        return false;
    }
}
