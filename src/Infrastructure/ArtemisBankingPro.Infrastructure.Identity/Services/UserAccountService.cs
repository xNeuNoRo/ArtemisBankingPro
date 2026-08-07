using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;

namespace ArtemisBankingPro.Infrastructure.Identity.Services;

/// <summary>
/// Servicio de cuentas de usuario: valida credenciales y roles. Implementa
/// <see cref="IUserAccountService"/> y utiliza <see cref="UserManager{TUser}"/> de ASP.NET Identity.
/// </summary>
public sealed class UserAccountService : IUserAccountService
{
    private readonly UserManager<AppUser> _userManager;

    public UserAccountService(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<LoginResult> ValidateCredentialsAsync(
        string userName,
        string password,
        IReadOnlyCollection<string> allowedRoles,
        CancellationToken ct = default
    )
    {
        AppUser? user = await _userManager.FindByNameAsync(userName);

        if (user is null || !await _userManager.CheckPasswordAsync(user, password))
        {
            return new LoginResult(LoginStatus.InvalidCredentials, null, null, null);
        }

        if (!user.Active)
        {
            return new LoginResult(LoginStatus.Inactive, user.Id, user.UserName, null);
        }

        IList<string> roles = await _userManager.GetRolesAsync(user);
        string? role = roles.FirstOrDefault(allowedRoles.Contains);

        if (role is null)
        {
            return new LoginResult(LoginStatus.RoleNotAllowed, user.Id, user.UserName, null);
        }

        return new LoginResult(LoginStatus.Success, user.Id, user.UserName, role);
    }
}
