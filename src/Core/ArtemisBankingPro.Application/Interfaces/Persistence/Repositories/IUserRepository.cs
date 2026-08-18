using ArtemisBankingPro.Domain.Common.Pagination;

namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

/// <summary>
/// Consultas de usuarios gestionadas por Identity.
/// </summary>
public interface IUserRepository {
    Task<UserListDto?> GetByUserNameAsync(string userName, CancellationToken ct = default);

    Task<UserListDto?> GetByIdAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<UserListDto>> GetByIdsAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken ct = default
    );

    Task<UserListDto?> GetByIdentityDocumentAsync(
        string document,
        CancellationToken ct = default
    );

    Task<bool> ExistsByUserNameAsync(string userName, CancellationToken ct = default);

    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default);

    Task<bool> ExistsByIdentityDocumentAsync(string document, CancellationToken ct = default);

    /// <summary>Listado paginado sin usuarios rol Comercio, opcionalmente filtrado por rol.</summary>
    Task<PageResult<UserListDto>> GetPagedAsync(
        string? role,
        PageRequest page,
        CancellationToken ct = default
    );

    /// <summary>Listado paginado de usuarios con rol Comercio.</summary>
    Task<PageResult<UserListDto>> GetCommerceUsersPagedAsync(
        PageRequest page,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct = default);

    /// <summary>Cantidad de usuarios con rol Cliente y estado activo.</summary>
    Task<int> CountActiveClientsAsync(CancellationToken ct = default);

    /// <summary>Conteos de usuarios con rol Cliente por estado (dashboard administrativo).</summary>
    Task<ClientStatusCounts> GetClientStatusCountsAsync(CancellationToken ct = default);

    /// <summary>
    /// Identificadores de usuarios con rol Cliente y estado activo, para
    /// calcular la deuda promedio solo sobre clientes activos (spec §559-569).
    /// </summary>
    Task<IReadOnlyList<string>> GetActiveClientIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// Página de clientes activos elegibles por identificación y por un conjunto
    /// de IDs previamente calculado por Application/Persistence.
    /// </summary>
    Task<PageResult<UserListDto>> GetActiveClientsPagedAsync(
        IReadOnlyCollection<string> clientIds,
        string? identification,
        PageRequest page,
        CancellationToken ct = default
    );

}
