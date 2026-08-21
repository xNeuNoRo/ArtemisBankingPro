using System.Data.Common;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Identity.Services;

/// <summary>
/// Enlista IdentityContext en la transacción iniciada por BankingDbContext.
/// </summary>
public sealed class IdentityTransactionParticipant : IDbTransactionParticipant {
    private readonly IdentityContext _context;

    public IdentityTransactionParticipant(IdentityContext context) {
        _context = context;
    }

    public void Enlist(DbTransaction transaction) {
        if (!ReferenceEquals(_context.Database.GetDbConnection(), transaction.Connection)) {
            throw new InvalidOperationException(
                "IdentityContext y BankingDbContext deben compartir la misma conexión SQL."
            );
        }

        _context.Database.UseTransaction(transaction);
    }

    public void ClearAfterRollback() => _context.ChangeTracker.Clear();
}
