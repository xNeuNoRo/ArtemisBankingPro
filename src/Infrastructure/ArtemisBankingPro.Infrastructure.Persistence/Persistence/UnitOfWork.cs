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
    private readonly IReadOnlyCollection<IDbTransactionParticipant> _participants;

    public UnitOfWork(BankingDbContext context) {
        _context = context;
        _participants = [];
    }

    public UnitOfWork(
        BankingDbContext context,
        IEnumerable<IDbTransactionParticipant> participants
    ) {
        _context = context;
        _participants = participants.ToArray();
    }

    public async Task<Result<T>> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<Result<T>>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default
    ) {
        await using IDbContextTransaction transaction =
            await _context.Database.BeginTransactionAsync(isolationLevel, ct);
        Result<T> result;
        bool commitStarted = false;
        try {
            EnlistParticipants(transaction);
            result = await operation(ct);

            if (result.IsFailure) {
                await RollbackAsync(transaction, CancellationToken.None);
                return result;
            }

            await _context.SaveChangesAsync(ct);
            // Once SaveChanges has succeeded, the commit must not be cancelled
            // by the request token. A cancellation here must not turn a
            // committed financial operation into an apparent failure.
            commitStarted = true;
            await transaction.CommitAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException) {
            if (commitStarted) {
                ClearTrackedState();
                throw;
            }

            await RollbackAsync(transaction, CancellationToken.None);
            return Result.Failure<T>(ConcurrencyConflict());
        }
        catch (DbUpdateException ex) when (IsSqlServerConflict(ex)) {
            if (commitStarted) {
                ClearTrackedState();
                throw;
            }

            await RollbackAsync(transaction, CancellationToken.None);
            return Result.Failure<T>(DatabaseConflict(ex));
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (IsSqlServerConflict(ex)) {
            if (commitStarted) {
                ClearTrackedState();
                throw;
            }

            await RollbackAsync(transaction, CancellationToken.None);
            return Result.Failure<T>(DatabaseConflict(ex));
        }
        catch {
            if (commitStarted) {
                ClearTrackedState();
                throw;
            }

            await RollbackAsync(transaction, CancellationToken.None);
            throw;
        }

        await _context.DispatchDeferredDomainEventsAsync(CancellationToken.None);
        return result;
    }

    public async Task<Result> ExecuteInTransactionAsync(
        Func<CancellationToken, Task<Result>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default
    ) {
        await using IDbContextTransaction transaction =
            await _context.Database.BeginTransactionAsync(isolationLevel, ct);
        Result result;
        bool commitStarted = false;
        try {
            EnlistParticipants(transaction);
            result = await operation(ct);

            if (result.IsFailure) {
                await RollbackAsync(transaction, CancellationToken.None);
                return result;
            }

            await _context.SaveChangesAsync(ct);
            commitStarted = true;
            await transaction.CommitAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException) {
            if (commitStarted) {
                ClearTrackedState();
                throw;
            }

            await RollbackAsync(transaction, CancellationToken.None);
            return Result.Failure(ConcurrencyConflict());
        }
        catch (DbUpdateException ex) when (IsSqlServerConflict(ex)) {
            if (commitStarted) {
                ClearTrackedState();
                throw;
            }

            await RollbackAsync(transaction, CancellationToken.None);
            return Result.Failure(DatabaseConflict(ex));
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (IsSqlServerConflict(ex)) {
            if (commitStarted) {
                ClearTrackedState();
                throw;
            }

            await RollbackAsync(transaction, CancellationToken.None);
            return Result.Failure(DatabaseConflict(ex));
        }
        catch {
            if (commitStarted) {
                ClearTrackedState();
                throw;
            }

            await RollbackAsync(transaction, CancellationToken.None);
            throw;
        }

        await _context.DispatchDeferredDomainEventsAsync(CancellationToken.None);
        return result;
    }

    private static DomainError ConcurrencyConflict() =>
        DomainError.Conflict(
            "Concurrency.Conflict",
            "La operación no pudo completarse porque los datos fueron modificados concurrentemente. Reintente la operación."
        );

    private static DomainError DatabaseConflict(Exception exception) =>
        ConflictKind(exception) switch {
            SqlServerConflictKind.Deadlock => DomainError.Conflict(
                "Concurrency.Deadlock",
                "La operación encontró un bloqueo concurrente. Reintente con la misma clave de idempotencia."
            ),
            SqlServerConflictKind.Unique => DomainError.Conflict(
                "Persistence.UniqueConflict",
                "La operación entra en conflicto con un registro existente."
            ),
            _ => ConcurrencyConflict(),
        };

    private void EnlistParticipants(IDbContextTransaction transaction) {
        foreach (IDbTransactionParticipant participant in _participants) {
            participant.Enlist(transaction.GetDbTransaction());
        }
    }

    private async Task RollbackAsync(IDbContextTransaction transaction, CancellationToken ct) {
        try {
            await transaction.RollbackAsync(ct);
        }
        finally {
            ClearTrackedState();
        }
    }

    private void ClearTrackedState() {
        _context.ChangeTracker.Clear();
        _context.ClearDeferredDomainEvents();
        foreach (IDbTransactionParticipant participant in _participants) {
            participant.ClearAfterRollback();
        }
    }

    /// <summary>
    /// Detecta conflictos SQL Server envueltos por EF Core. Los deadlocks no se
    /// reintentan aquí: el caller debe decidir si existe idempotencia suficiente
    /// para repetir la operación completa.
    /// </summary>
    private static bool IsSqlServerConflict(Exception ex) {
        return ConflictKind(ex) != SqlServerConflictKind.None;
    }

    private static SqlServerConflictKind ConflictKind(Exception ex) {
        for (Exception? current = ex; current is not null; current = current.InnerException) {
            if (
                current is Microsoft.Data.SqlClient.SqlException sqlException
                && sqlException.Number is 1205 or 2601 or 2627
            ) {
                return sqlException.Number == 1205
                    ? SqlServerConflictKind.Deadlock
                    : SqlServerConflictKind.Unique;
            }
        }

        return SqlServerConflictKind.None;
    }

    private enum SqlServerConflictKind {
        None,
        Deadlock,
        Unique,
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose() { }
}
