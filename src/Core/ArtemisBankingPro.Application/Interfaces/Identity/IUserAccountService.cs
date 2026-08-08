using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Interfaces.Identity;

public enum LoginStatus
{
    Success = 1,
    InvalidCredentials = 2,
    Inactive = 3,
    RoleNotAllowed = 4,
}

public sealed record LoginResult(LoginStatus Status, string? UserId, string? UserName, string? Role)
{
    public bool IsSuccess => Status == LoginStatus.Success;
}

/// <summary>
/// Datos del usuario necesarios para el flujo de restablecimiento de contraseña.
/// </summary>
public sealed record PasswordResetUserInfo(
    string UserId,
    string UserName,
    string Email,
    string FullName
);

/// <summary>Resultado de la creación de un usuario (inactivo hasta activar).</summary>
public sealed record CreatedUserInfo(
    string UserId,
    string UserName,
    string Email,
    string Role,
    bool IsActive,
    string FullName
);

/// <summary>
/// Verificación de credenciales y operaciones de cuenta del usuario.
/// </summary>
public interface IUserAccountService
{
    /// <summary>
    /// Valida usuario, contraseña, estado activo y rol permitido.
    /// Devuelve información mínima para emitir el token.
    /// </summary>
    Task<LoginResult> ValidateCredentialsAsync(
        string userName,
        string password,
        IReadOnlyCollection<string> allowedRoles,
        CancellationToken ct = default
    );

    /// <summary>
    /// Busca un usuario por nombre de usuario para el flujo de restablecimiento
    /// de contraseña. Devuelve <c>null</c> si no existe o no tiene rol permitido.
    /// </summary>
    Task<PasswordResetUserInfo?> FindForPasswordResetAsync(
        string userName,
        IReadOnlyCollection<string> allowedRoles,
        CancellationToken ct = default
    );

    /// <summary>
    /// Activa o desactiva la cuenta de un usuario. Inactivar impide el inicio
    /// de sesión pero no elimina productos ni historial.
    /// </summary>
    Task<Result> SetActiveAsync(string userId, bool isActive, CancellationToken ct = default);

    /// <summary>
    /// Cambia la contraseña de un usuario
    /// </summary>
    Task<Result> ChangePasswordAsync(
        string userId,
        string newPassword,
        CancellationToken ct = default
    );

    /// <summary>
    /// Crea un usuario inactivo con el rol indicado. La unicidad de nombre de
    /// usuario, correo y cédula se valida en el caller y se refuerza en la BD.
    /// </summary>
    Task<Result<CreatedUserInfo>> CreateUserAsync(
        string firstName,
        string lastName,
        string identityDocument,
        string email,
        string userName,
        string password,
        string role,
        CancellationToken ct = default
    );

    /// <summary>
    /// Actualiza datos de perfil de un usuario sin cambiar su rol.
    /// </summary>
    Task<Result> UpdateUserProfileAsync(
        string userId,
        string firstName,
        string lastName,
        string identityDocument,
        string email,
        string userName,
        CancellationToken ct = default
    );

    /// <summary>
    /// Elimina un usuario sin productos ni historial.
    /// </summary>
    Task<Result> DeleteUserAsync(string userId, CancellationToken ct = default);
}
