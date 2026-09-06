using System.Data.Common;

namespace ArtemisBankingPro.Application.Interfaces.Persistence;

/// <summary>
/// Enlista un contexto de persistencia adicional en la transacción SQL de
/// <see cref="IUnitOfWork"/>. Los participantes deben compartir la misma
/// conexión física que el contexto principal.
/// </summary>
public interface IDbTransactionParticipant {
    void Enlist(DbTransaction transaction);

    void ClearAfterRollback();
}
