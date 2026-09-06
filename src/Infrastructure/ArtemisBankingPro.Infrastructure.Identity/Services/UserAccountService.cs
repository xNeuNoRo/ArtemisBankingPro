using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using ArtemisBankingPro.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ArtemisBankingPro.Infrastructure.Identity.Services;

/// <summary>
/// Servicio de cuentas de usuario: valida credenciales y roles, y gestiona
/// activación y restablecimiento de contraseña. Implementa
/// <see cref="IUserAccountService"/> y utiliza <see cref="UserManager{TUser}"/> de ASP.NET Identity.
/// </summary>
public sealed class UserAccountService : IUserAccountService {
    private readonly UserManager<AppUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly IdentityContext _context;
    private readonly TimeProvider _timeProvider;

    public UserAccountService(
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole> roleManager,
        SignInManager<AppUser> signInManager,
        IdentityContext context,
        TimeProvider timeProvider
    ) {
        _userManager = userManager;
        _roleManager = roleManager;
        _signInManager = signInManager;
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<LoginResult> ValidateCredentialsAsync(
        string userName,
        string password,
        IReadOnlyCollection<string> allowedRoles,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        AppUser? user = await _userManager.FindByNameAsync(userName);

        if (user is null) {
            return new LoginResult(LoginStatus.InvalidCredentials, null, null, null);
        }

        if (!user.Active) {
            return new LoginResult(LoginStatus.Inactive, user.Id, user.UserName, null);
        }

        SignInResult signInResult = await _signInManager.CheckPasswordSignInAsync(
            user,
            password,
            lockoutOnFailure: true
        );
        if (!signInResult.Succeeded) {
            return new LoginResult(LoginStatus.InvalidCredentials, null, null, null);
        }

        IList<string> roles = await _userManager.GetRolesAsync(user);
        string? role = roles.FirstOrDefault(allowedRoles.Contains);

        if (role is null) {
            return new LoginResult(LoginStatus.RoleNotAllowed, user.Id, user.UserName, null);
        }

        return new LoginResult(
            LoginStatus.Success,
            user.Id,
            user.UserName,
            role
        );
    }

    public async Task<Result> SignInWebAppAsync(
        string userId,
        string expectedRole,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        AppUser? user = await _userManager.FindByIdAsync(userId);
        if (user is null || !user.Active) {
            return Result.Failure(
                DomainError.Unauthorized(
                    "Auth.Inactive",
                    "Su cuenta se encuentra inactiva. Debe activar su cuenta mediante el enlace "
                        + "enviado a su correo electrónico registrado para poder acceder al sistema."
                )
            );
        }

        IList<string> roles = await _userManager.GetRolesAsync(user);
        if (!RoleSets.Mvc.Contains(expectedRole, StringComparer.Ordinal)
            || !roles.Contains(expectedRole, StringComparer.Ordinal)) {
            return Result.Failure(
                DomainError.Forbidden(
                    "Auth.RoleNotAllowed",
                    "Este usuario no tiene permisos para acceder a la aplicación web."
                )
            );
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        return Result.Success();
    }

    public async Task<Result> SignOutWebAppAsync(CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        await _signInManager.SignOutAsync();
        return Result.Success();
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
        IdentityResult result = await _userManager.UpdateSecurityStampAsync(user);

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

        IDbContextTransaction? ownTransaction = null;
        try {
            if (_context.Database.CurrentTransaction is null) {
                ownTransaction = await _context.Database.BeginTransactionAsync(ct);
            }

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
            if (!addResult.Succeeded) {
                return Result.Failure(
                    DomainError.Conflict(
                        "User.PasswordChangeFailed",
                        "No fue posible cambiar la contraseña del usuario."
                    )
                );
            }

            IdentityResult stampResult = await _userManager.UpdateSecurityStampAsync(user);
            if (!stampResult.Succeeded) {
                return Result.Failure(
                    DomainError.Conflict(
                        "User.PasswordChangeFailed",
                        "No fue posible invalidar las sesiones del usuario."
                    )
                );
            }

            if (ownTransaction is not null) {
                await ownTransaction.CommitAsync(ct);
            }

            return Result.Success();
        }
        catch (DbUpdateConcurrencyException) {
            return Result.Failure(
                DomainError.Conflict(
                    "Concurrency.Conflict",
                    "La cuenta cambió mientras se actualizaba la contraseña. Intente nuevamente."
                )
            );
        }
        finally {
            if (ownTransaction is not null) {
                await ownTransaction.DisposeAsync();
            }
        }
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
        if (!await _roleManager.RoleExistsAsync(role)) {
            return Result.Failure<CreatedUserInfo>(
                DomainError.Conflict(
                    "User.RoleNotFound",
                    "El rol indicado no está configurado."
                )
            );
        }

        try {
            var user = new AppUser {
                UserName = userName,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                IdentityDocument = identityDocument,
                Active = false,
                CreatedAt = _timeProvider.GetUtcNow(),
            };

            IdentityResult createResult = await _userManager.CreateAsync(user, password);
            if (!createResult.Succeeded) {
                return Result.Failure<CreatedUserInfo>(ToDomainError(createResult));
            }

            IdentityResult roleResult = await _userManager.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded) {
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
        catch (DbUpdateConcurrencyException) {
            return Result.Failure<CreatedUserInfo>(
                DomainError.Conflict(
                    "Concurrency.Conflict",
                    "La creación del usuario entró en conflicto con otra operación. Reintente."
                )
            );
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex)) {
            return Result.Failure<CreatedUserInfo>(
                DomainError.Conflict(
                    "User.Duplicate",
                    "Ya existe un usuario registrado con alguno de los datos indicados."
                )
            );
        }
        catch (DbUpdateException ex) when (IsConcurrencyViolation(ex)) {
            return Result.Failure<CreatedUserInfo>(
                DomainError.Conflict(
                    "Concurrency.Conflict",
                    "La creación del usuario entró en conflicto con otra operación. Reintente."
                )
            );
        }
        catch (InvalidOperationException ex) when (HasSqlServerError(ex, 2601, 2627)) {
            return Result.Failure<CreatedUserInfo>(
                DomainError.Conflict(
                    "User.Duplicate",
                    "Ya existe un usuario registrado con alguno de los datos indicados."
                )
            );
        }
        catch (InvalidOperationException ex) when (HasSqlServerError(ex, 1205, 3960)) {
            return Result.Failure<CreatedUserInfo>(
                DomainError.Conflict(
                    "Concurrency.Conflict",
                    "La creación del usuario entró en conflicto con otra operación. Reintente."
                )
            );
        }
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

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        HasSqlServerError(exception, 2601, 2627);

    private static bool IsConcurrencyViolation(DbUpdateException exception) =>
        HasSqlServerError(exception, 1205, 3960);

    private static bool HasSqlServerError(Exception exception, params int[] numbers) {
        for (Exception? current = exception; current is not null; current = current.InnerException) {
            if (current is SqlException sqlException && numbers.Contains(sqlException.Number)) {
                return true;
            }
        }

        return false;
    }
}
