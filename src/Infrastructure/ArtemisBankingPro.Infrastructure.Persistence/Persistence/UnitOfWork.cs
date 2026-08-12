using System.Data;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Persistence;

/// <summary>
/// Representa una unidad de trabajo que encapsula la lógica de transacciones y operaciones de persistencia en la base de datos.
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
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            async executionCt => {
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    isolationLevel,
                    executionCt
                );
                try {
                    Result<T> result = await operation(executionCt);
                    await _context.SaveChangesAsync(executionCt);
                    await transaction.CommitAsync(executionCt);
                    return result;
                }
                catch (DbUpdateConcurrencyException) {
                    await transaction.RollbackAsync(executionCt);
                    return Result.Failure<T>(
                        DomainError.Conflict(
                            "Concurrency.Conflict",
                            "La operación no pudo completarse porque los datos fueron modificados concurrentemente. Reintente la operación."
                        )
                    );
                }
                catch {
                    await transaction.RollbackAsync(executionCt);
                    throw;
                }
            },
            ct
        );
    }

    public async Task<Result> ExecuteInTransactionAsync(
        Func<CancellationToken, Task<Result>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default
    ) {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            async executionCt => {
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    isolationLevel,
                    executionCt
                );
                try {
                    Result result = await operation(executionCt);
                    await _context.SaveChangesAsync(executionCt);
                    await transaction.CommitAsync(executionCt);
                    return result;
                }
                catch (DbUpdateConcurrencyException) {
                    await transaction.RollbackAsync(executionCt);
                    return Result.Failure(
                        DomainError.Conflict(
                            "Concurrency.Conflict",
                            "La operación no pudo completarse porque los datos fueron modificados concurrentemente. Reintente la operación."
                        )
                    );
                }
                catch {
                    await transaction.RollbackAsync(executionCt);
                    throw;
                }
            },
            ct
        );
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose() { }
}
