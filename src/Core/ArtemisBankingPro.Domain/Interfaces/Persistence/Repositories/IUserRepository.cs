using ArtemisBankingPro.Domain.Common.Pagination;

namespace ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;

/// <summary>
/// Consultas de usuarios gestionadas por Identity.
/// </summary>
public interface IUserRepository {
    Task<UserListDto?> GetByUserNameAsync(string userName, CancellationToken ct = default);

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
}
