using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Application.Features.HermesPay.DTOs;
using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

public interface ICreditCardRepository : IGenericRepository<CreditCard> {
    /// <summary>Consumos de una tarjeta paginados, más recientes primero.</summary>
    Task<PageResult<CardConsumptionView>> GetConsumptionsPagedAsync(
        int creditCardId,
        PageRequest page,
        CancellationToken ct = default
    );

    /// <summary>
    /// Consumos recibidos por un comercio vía Hermes Pay (spec §41,
    /// GET /pay/get-transactions), paginados y más recientes primero. Cada
    /// elemento expone el id, la fecha, el monto, los últimos cuatro dígitos
    /// de la tarjeta y el estado (APROBADO / RECHAZADO).
    /// </summary>
    Task<PageResult<CommerceTransactionDto>> GetConsumptionsByMerchantPagedAsync(
        int merchantId,
        PageRequest page,
        CancellationToken ct = default
    );

    /// <summary>
    /// Listado paginado de tarjetas con filtro por cliente y estado.
    /// Por defecto las activas aparecen primero.
    /// </summary>
    Task<PageResult<CreditCardSummaryDto>> GetPagedAsync(
        string? customerUserId,
        CreditCardStatus? status,
        PageRequest page,
        CancellationToken ct = default
    );
    Task<CreditCard?> GetByPanFingerprintAsync(
        string panFingerprint,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<CreditCard>> GetByCustomerAsync(
        string customerUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Resúmenes de tarjetas activas del cliente para el Home sin materializar
    /// entidades completas (huella del PAN ni digesto del CVC).
    /// </summary>
    Task<IReadOnlyList<CreditCardSummaryDto>> GetActiveSummariesByCustomerAsync(
        string customerUserId,
        CancellationToken ct = default
    );

    /// <summary>Suma de la deuda de todas las tarjetas activas.</summary>
    Task<Money> GetTotalActiveDebtAsync(CancellationToken ct = default);

    /// <summary>Suma de la deuda de las tarjetas activas de un cliente.</summary>
    Task<Money> GetClientActiveDebtAsync(string customerUserId, CancellationToken ct = default);
}
