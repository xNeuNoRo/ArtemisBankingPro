using System.Data;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Interfaces.Persistence;

/// <summary>
/// Unit of Work para ejecutar operaciones atómicas sin reintentar
/// automáticamente escrituras cuyo resultado pueda ser desconocido.
/// </summary>
public interface IUnitOfWork : IDisposable, IAsyncDisposable {
    /// <summary>
    /// Ejecuta una operación que devuelve un valor dentro de una transacción.
    /// Se confirma solo si la operación termina con éxito; cualquier excepción
    /// revierte y se propaga. El caller decide si un reintento es seguro según
    /// la idempotencia de la operación.
    /// </summary>
    Task<Result<T>> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<Result<T>>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default
    );

    /// <inheritdoc cref="ExecuteInTransactionAsync{T}"/>
    Task<Result> ExecuteInTransactionAsync(
        Func<CancellationToken, Task<Result>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default
    );
}
