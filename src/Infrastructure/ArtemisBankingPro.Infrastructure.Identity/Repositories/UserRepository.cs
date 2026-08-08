using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Identity.Repositories;

/// <summary>
/// Repositorio de usuarios de la aplicación. Implementa <see cref="IUserRepository"/> y utiliza
/// <see cref="IdentityContext"/> para acceder a la base de datos.
/// </summary>
public sealed class UserRepository : IUserRepository
{
    private readonly IdentityContext _context;
    private readonly UserManager<AppUser> _userManager;

    public UserRepository(IdentityContext context, UserManager<AppUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<UserListDto?> GetByUserNameAsync(
        string userName,
        CancellationToken ct = default
    )
    {
        string normalized = _userManager.NormalizeName(userName);

        return await _context
            .Users.Where(user => user.NormalizedUserName == normalized)
            .Select(user => new UserListDto(
                user.Id,
                user.UserName!,
                user.IdentityDocument,
                user.FirstName,
                user.LastName,
                user.Email!,
                _context
                    .UserRoles.Where(userRole => userRole.UserId == user.Id)
                    .Join(
                        _context.Roles,
                        userRole => userRole.RoleId,
                        role => role.Id,
                        (_, role) => role.Name!
                    )
                    .FirstOrDefault()
                    ?? string.Empty,
                user.Active,
                user.CreatedAt
            ))
            .FirstOrDefaultAsync(ct);
    }

    public Task<bool> ExistsByUserNameAsync(string userName, CancellationToken ct = default) =>
        _context.Users.AnyAsync(
            user => user.NormalizedUserName == _userManager.NormalizeName(userName),
            ct
        );

    public async Task<IReadOnlyList<UserListDto>> GetByIdsAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken ct = default
    )
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        return await _context
            .Users.Where(user => userIds.Contains(user.Id))
            .Select(user => new UserListDto(
                user.Id,
                user.UserName!,
                user.IdentityDocument,
                user.FirstName,
                user.LastName,
                user.Email!,
                _context
                    .UserRoles.Where(userRole => userRole.UserId == user.Id)
                    .Join(
                        _context.Roles,
                        userRole => userRole.RoleId,
                        role => role.Id,
                        (_, role) => role.Name!
                    )
                    .FirstOrDefault()
                    ?? string.Empty,
                user.Active,
                user.CreatedAt
            ))
            .ToListAsync(ct);
    }

    public Task<UserListDto?> GetByIdentityDocumentAsync(
        string document,
        CancellationToken ct = default
    ) =>
        _context
            .Users.Where(user => user.IdentityDocument == document)
            .Select(user => new UserListDto(
                user.Id,
                user.UserName!,
                user.IdentityDocument,
                user.FirstName,
                user.LastName,
                user.Email!,
                _context
                    .UserRoles.Where(userRole => userRole.UserId == user.Id)
                    .Join(
                        _context.Roles,
                        userRole => userRole.RoleId,
                        role => role.Id,
                        (_, role) => role.Name!
                    )
                    .FirstOrDefault()
                    ?? string.Empty,
                user.Active,
                user.CreatedAt
            ))
            .FirstOrDefaultAsync(ct);

    public async Task<int> CountActiveClientsAsync(CancellationToken ct = default)
    {
        var clientRoleId = await _context
            .Roles.Where(role => role.Name == nameof(Roles.Cliente))
            .Select(role => role.Id)
            .FirstOrDefaultAsync(ct);

        if (clientRoleId is null)
        {
            return 0;
        }

        return await _context
            .Users.CountAsync(
                user => user.Active
                    && _context.UserRoles.Any(userRole =>
                        userRole.UserId == user.Id && userRole.RoleId == clientRoleId
                    ),
                ct
            );
    }

    public Task<UserListDto?> GetByIdAsync(string userId, CancellationToken ct = default) =>
        _context
            .Users.Where(user => user.Id == userId)
            .Select(user => new UserListDto(
                user.Id,
                user.UserName!,
                user.IdentityDocument,
                user.FirstName,
                user.LastName,
                user.Email!,
                _context
                    .UserRoles.Where(userRole => userRole.UserId == user.Id)
                    .Join(
                        _context.Roles,
                        userRole => userRole.RoleId,
                        role => role.Id,
                        (_, role) => role.Name!
                    )
                    .FirstOrDefault()
                    ?? string.Empty,
                user.Active,
                user.CreatedAt
            ))
            .FirstOrDefaultAsync(ct);

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default) =>
        _context.Users.AnyAsync(
            user => user.NormalizedEmail == _userManager.NormalizeEmail(email),
            ct
        );

    public Task<bool> ExistsByIdentityDocumentAsync(
        string document,
        CancellationToken ct = default
    ) => _context.Users.AnyAsync(user => user.IdentityDocument == document, ct);

    public async Task<PageResult<UserListDto>> GetPagedAsync(
        string? role,
        PageRequest page,
        CancellationToken ct = default
    )
    {
        IQueryable<AppUser> query = _context.Users.Where(user =>
            !_context
                .UserRoles.Where(userRole => userRole.UserId == user.Id)
                .Join(
                    _context.Roles,
                    userRole => userRole.RoleId,
                    item => item.Id,
                    (_, item) => item.Name!
                )
                .Contains(nameof(Roles.Comercio))
        );

        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(user =>
                _context
                    .UserRoles.Where(userRole => userRole.UserId == user.Id)
                    .Join(
                        _context.Roles,
                        userRole => userRole.RoleId,
                        item => item.Id,
                        (_, item) => item.Name!
                    )
                    .Contains(role)
            );
        }

        return await ToPageAsync(query, page, ct);
    }

    public async Task<PageResult<UserListDto>> GetCommerceUsersPagedAsync(
        PageRequest page,
        CancellationToken ct = default
    ) =>
        await ToPageAsync(
            _context.Users.Where(user =>
                _context
                    .UserRoles.Where(userRole => userRole.UserId == user.Id)
                    .Join(
                        _context.Roles,
                        userRole => userRole.RoleId,
                        item => item.Id,
                        (_, item) => item.Name!
                    )
                    .Contains(nameof(Roles.Comercio))
            ),
            page,
            ct
        );

    public async Task<IReadOnlyList<string>> GetRolesAsync(
        string userId,
        CancellationToken ct = default
    ) =>
        await _context
            .UserRoles.Where(userRole => userRole.UserId == userId)
            .Join(
                _context.Roles,
                userRole => userRole.RoleId,
                role => role.Id,
                (_, role) => role.Name!
            )
            .ToListAsync(ct);

    private async Task<PageResult<UserListDto>> ToPageAsync(
        IQueryable<AppUser> query,
        PageRequest page,
        CancellationToken ct
    )
    {
        int totalCount = await query.CountAsync(ct);
        List<UserListDto> items = await query
            .OrderByDescending(user => user.CreatedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(user => new UserListDto(
                user.Id,
                user.UserName!,
                user.IdentityDocument,
                user.FirstName,
                user.LastName,
                user.Email!,
                _context
                    .UserRoles.Where(userRole => userRole.UserId == user.Id)
                    .Join(
                        _context.Roles,
                        userRole => userRole.RoleId,
                        role => role.Id,
                        (_, role) => role.Name!
                    )
                    .FirstOrDefault()
                    ?? string.Empty,
                user.Active,
                user.CreatedAt
            ))
            .ToListAsync(ct);

        return new PageResult<UserListDto>(items, totalCount, page.Page, page.PageSize);
    }
}
