using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Merchants.Enums;

namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

public interface IMerchantRepository : IGenericRepository<Merchant> {
    Task<Merchant?> GetByRncAsync(string rnc, CancellationToken ct = default);

    Task<bool> ExistsByRncAsync(string rnc, CancellationToken ct = default);

    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default);

    Task<Merchant?> GetByAssociatedUserIdAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Listado paginado de comercios, del más reciente al más antiguo.
    /// Un <paramref name="status"/> nulo devuelve comercios de todos los
    /// estados; el caller traduce el parámetro del contrato (§40:
    /// activo/inactivo/todos, por defecto activo).
    /// </summary>
    Task<PageResult<MerchantSummaryDto>> GetPagedAsync(
        MerchantStatus? status,
        PageRequest page,
        CancellationToken ct = default
    );
}
