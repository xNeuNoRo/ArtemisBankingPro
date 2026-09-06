namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

/// <summary>
/// Persistencia de nonces de confirmación. Las operaciones de escritura se
/// ejecutan dentro de la transacción del <see cref="IUnitOfWork"/>.
/// </summary>
public interface IConfirmationTokenRepository {
    Task<ConfirmationToken?> GetByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default
    );

    Task AddAsync(ConfirmationToken token, CancellationToken cancellationToken = default);
}
