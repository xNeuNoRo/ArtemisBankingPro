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
/// Verificación de credenciales
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
}
