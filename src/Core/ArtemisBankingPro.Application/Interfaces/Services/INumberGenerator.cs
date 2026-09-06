namespace ArtemisBankingPro.Application.Interfaces.Services;

/// <summary>
/// Genera identificadores numéricos compartidos (cuentas y préstamos usan el
/// mismo espacio de 9 dígitos; las tarjetas usan el contador con BIN propio).
/// </summary>
public interface INumberGenerator {
    Task<string> NextAccountNumberAsync(CancellationToken ct = default);

    Task<string> NextLoanNumberAsync(CancellationToken ct = default);

    Task<string> NextCardNumberAsync(CancellationToken ct = default);
}
