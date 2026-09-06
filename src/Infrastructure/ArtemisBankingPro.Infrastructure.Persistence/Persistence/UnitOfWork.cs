using System.Data;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ArtemisBankingPro.Infrastructure.Persistence.Persistence;

/// <summary>
/// Unidad de trabajo que encapsula transacciones y persistencia.
///
/// Contrato (ADR-002):
/// - La transacción se confirma <b>solo</b> si la operación termina en éxito;
///   un <c>Result.Failure</c> revierte y se devuelve tal cual.
/// - Las escrituras financieras no se reintentan a ciegas: un conflicto de
///   concurrencia (<c>rowversion</c>) o de unicidad se traduce a un resultado
///   de conflicto estable; el reintento lo resuelve la capa de idempotencia.
/// - Tras un rollback se descarta el estado rastreado para que una operación
///   posterior en el mismo scope no persista mutaciones abortadas.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork {
    private readonly BankingDbContext _context;

    public UnitOfWork(BankingDbContext context) {
        _context = context;
    }

    public async Task<Result<T>> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<Result<T>>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default
    ) {
        await using IDbContextTransaction transaction =
            await _context.Database.BeginTransactionAsync(isolationLevel, ct);
        try {
            Result<T> result = await operation(ct);

            if (result.IsFailure) {
                await RollbackAsync(transaction, ct);
                return result;
            }

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException) {
            await RollbackAsync(transaction, ct);
            return Result.Failure<T>(ConcurrencyConflict());
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex)) {
            await RollbackAsync(transaction, ct);
            return Result.Failure<T>(ConcurrencyConflict());
        }
        catch {
            await RollbackAsync(transaction, ct);
            throw;
        }
    }

    public async Task<Result> ExecuteInTransactionAsync(
        Func<CancellationToken, Task<Result>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default
    ) {
        await using IDbContextTransaction transaction =
            await _context.Database.BeginTransactionAsync(isolationLevel, ct);
        try {
            Result result = await operation(ct);

            if (result.IsFailure) {
                await RollbackAsync(transaction, ct);
                return result;
            }

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException) {
            await RollbackAsync(transaction, ct);
            return Result.Failure(ConcurrencyConflict());
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex)) {
            await RollbackAsync(transaction, ct);
            return Result.Failure(ConcurrencyConflict());
        }
        catch {
            await RollbackAsync(transaction, ct);
            throw;
        }
    }

    private static DomainError ConcurrencyConflict() =>
        DomainError.Conflict(
            "Concurrency.Conflict",
            "La operación no pudo completarse porque los datos fueron modificados concurrentemente. Reintente la operación."
        );

    private async Task RollbackAsync(IDbContextTransaction transaction, CancellationToken ct) {
        try {
            await transaction.RollbackAsync(ct);
        }
        finally {
            _context.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Detecta violaciones de índice/clave única de SQL Server (2601/2627),
    /// que en EF Core llegan envueltas en <see cref="DbUpdateException"/>.
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlException
        && (sqlException.Number == 2601 || sqlException.Number == 2627);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose() { }
}
