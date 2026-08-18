using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;

namespace ArtemisBankingPro.Infrastructure.Identity.Services;

/// <summary>
/// Servicio de cuentas de usuario: valida credenciales y roles, y gestiona
/// activación y restablecimiento de contraseña. Implementa
/// <see cref="IUserAccountService"/> y utiliza <see cref="UserManager{TUser}"/> de ASP.NET Identity.
/// </summary>
public sealed class UserAccountService : IUserAccountService {
    private readonly UserManager<AppUser> _userManager;

    public UserAccountService(UserManager<AppUser> userManager) {
        _userManager = userManager;
    }

    public async Task<LoginResult> ValidateCredentialsAsync(
        string userName,
        string password,
        IReadOnlyCollection<string> allowedRoles,
        CancellationToken ct = default
    ) {
        AppUser? user = await _userManager.FindByNameAsync(userName);

        if (user is null || !await _userManager.CheckPasswordAsync(user, password)) {
            return new LoginResult(LoginStatus.InvalidCredentials, null, null, null);
        }

        if (!user.Active) {
            return new LoginResult(LoginStatus.Inactive, user.Id, user.UserName, null);
        }

        IList<string> roles = await _userManager.GetRolesAsync(user);
        string? role = roles.FirstOrDefault(allowedRoles.Contains);

        if (role is null) {
            return new LoginResult(LoginStatus.RoleNotAllowed, user.Id, user.UserName, null);
        }

        return new LoginResult(LoginStatus.Success, user.Id, user.UserName, role);
    }

    public async Task<PasswordResetUserInfo?> FindForPasswordResetAsync(
        string userName,
        IReadOnlyCollection<string> allowedRoles,
        CancellationToken ct = default
    ) {
        AppUser? user = await _userManager.FindByNameAsync(userName);
        if (user is null) {
            return null;
        }

        IList<string> roles = await _userManager.GetRolesAsync(user);
        if (!roles.Any(allowedRoles.Contains)) {
            return null;
        }

        return new PasswordResetUserInfo(user.Id, user.UserName!, user.Email!, user.FullName);
    }

    public async Task<bool?> GetActiveAsync(
        string userId,
        CancellationToken ct = default
    ) {
        AppUser? user = await _userManager.FindByIdAsync(userId);
        return user?.Active;
    }

    public async Task<Result> SetActiveAsync(
        string userId,
        bool isActive,
        CancellationToken ct = default
    ) {
        AppUser? user = await _userManager.FindByIdAsync(userId);
        if (user is null) {
            return Result.Failure(DomainError.NotFound("User.NotFound", "El usuario no existe."));
        }

        user.Active = isActive;
        IdentityResult result = await _userManager.UpdateAsync(user);

        return result.Succeeded
            ? Result.Success()
            : Result.Failure(
                DomainError.Conflict(
                    "User.StatusUpdateFailed",
                    "No fue posible actualizar el estado del usuario."
                )
            );
    }

    public async Task<Result> ChangePasswordAsync(
        string userId,
        string newPassword,
        CancellationToken ct = default
    ) {
        AppUser? user = await _userManager.FindByIdAsync(userId);
        if (user is null) {
            return Result.Failure(DomainError.NotFound("User.NotFound", "El usuario no existe."));
        }

        // RemovePasswordAsync + AddPasswordAsync no requieren token provider
        // (a diferencia de GeneratePasswordResetTokenAsync) y son equivalentes
        // para un cambio de contraseña administrativo tras validar el token
        // de restablecimiento del sistema.
        IdentityResult removeResult = await _userManager.RemovePasswordAsync(user);
        if (!removeResult.Succeeded) {
            return Result.Failure(
                DomainError.Conflict(
                    "User.PasswordChangeFailed",
                    "No fue posible cambiar la contraseña del usuario."
                )
            );
        }

        IdentityResult addResult = await _userManager.AddPasswordAsync(user, newPassword);

        return addResult.Succeeded
            ? Result.Success()
            : Result.Failure(
                DomainError.Conflict(
                    "User.PasswordChangeFailed",
                    "No fue posible cambiar la contraseña del usuario."
                )
            );
    }

    public async Task<Result<CreatedUserInfo>> CreateUserAsync(
        string firstName,
        string lastName,
        string identityDocument,
        string email,
        string userName,
        string password,
        string role,
        CancellationToken ct = default
    ) {
        var user = new AppUser {
            UserName = userName,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            IdentityDocument = identityDocument,
            Active = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        IdentityResult createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded) {
            return Result.Failure<CreatedUserInfo>(ToDomainError(createResult));
        }

        IdentityResult roleResult = await _userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded) {
            // El usuario no debe quedar creado sin rol.
            await _userManager.DeleteAsync(user);
            return Result.Failure<CreatedUserInfo>(
                DomainError.Conflict(
                    "User.RoleAssignmentFailed",
                    "No fue posible asignar el rol al usuario."
                )
            );
        }

        return Result.Success(
            new CreatedUserInfo(
                user.Id,
                user.UserName,
                user.Email,
                role,
                user.Active,
                user.FullName
            )
        );
    }

    public async Task<Result> UpdateUserProfileAsync(
        string userId,
        string firstName,
        string lastName,
        string identityDocument,
        string email,
        string userName,
        CancellationToken ct = default
    ) {
        AppUser? user = await _userManager.FindByIdAsync(userId);
        if (user is null) {
            return Result.Failure(DomainError.NotFound("User.NotFound", "El usuario no existe."));
        }

        user.FirstName = firstName;
        user.LastName = lastName;
        user.IdentityDocument = identityDocument;

        IdentityResult userNameResult = await _userManager.SetUserNameAsync(user, userName);
        if (!userNameResult.Succeeded) {
            return Result.Failure(ToDomainError(userNameResult));
        }

        IdentityResult emailResult = await _userManager.SetEmailAsync(user, email);
        if (!emailResult.Succeeded) {
            return Result.Failure(ToDomainError(emailResult));
        }

        IdentityResult updateResult = await _userManager.UpdateAsync(user);

        return updateResult.Succeeded
            ? Result.Success()
            : Result.Failure(ToDomainError(updateResult));
    }

    public async Task<Result> DeleteUserAsync(string userId, CancellationToken ct = default) {
        AppUser? user = await _userManager.FindByIdAsync(userId);
        if (user is null) {
            return Result.Failure(DomainError.NotFound("User.NotFound", "El usuario no existe."));
        }

        IdentityResult result = await _userManager.DeleteAsync(user);

        return result.Succeeded
            ? Result.Success()
            : Result.Failure(
                DomainError.Conflict("User.DeleteFailed", "No fue posible eliminar el usuario.")
            );
    }

    private static DomainError ToDomainError(IdentityResult result) {
        string code = result.Errors.FirstOrDefault()?.Code ?? "Unknown";
        string description = result.Errors.FirstOrDefault()?.Description ?? "Operación fallida.";

        return code is "DuplicateUserName" or "DuplicateEmail"
            ? DomainError.Conflict("User.Duplicate", description)
            : DomainError.Conflict("User.OperationFailed", description);
    }
}
