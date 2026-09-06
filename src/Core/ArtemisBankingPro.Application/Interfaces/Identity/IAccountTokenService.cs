using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Interfaces.Identity;

public enum AccountTokenType {
    Activation = 1,
    PasswordReset = 2,
}

public enum AccountTokenVerificationResult {
    /// <summary>Token válido: pertenece al usuario y propósito, sin expirar y sin usar.</summary>
    Valid = 1,

    /// <summary>Token inexistente o que no corresponde al usuario/propósito.</summary>
    Invalid = 2,

    /// <summary>Token expirado.</summary>
    Expired = 3,

    /// <summary>Token ya consumido.</summary>
    AlreadyUsed = 4,
}

/// <summary>Resultado de verificación que incluye el usuario al que pertenece el token.</summary>
public sealed record TokenVerification(AccountTokenVerificationResult Result, string? UserId) {
    public bool IsValid => Result == AccountTokenVerificationResult.Valid;
}

public sealed record PasswordResetTokenResult(
    string RawToken,
    string Email,
    string FullName
);

/// <summary>
/// Generación y verificación de tokens de activación y restablecimiento de
/// contraseña. Los tokens son aleatorios, de un solo uso, con vencimiento y
/// vinculados a usuario y propósito; solo se persiste su hash HMAC con clave.
/// Implementado por Infrastructure.Identity.
/// </summary>
public interface IAccountTokenService {
    /// <summary>
    /// Genera un token crudo (nunca persistido) e invalida tokens previos
    /// no usados del mismo usuario y propósito.
    /// </summary>
    Task<string> GenerateAsync(
        string userId,
        AccountTokenType type,
        CancellationToken ct = default
    );

    /// <summary>
    /// Desactiva la cuenta, invalida tokens de reset anteriores y genera el
    /// nuevo token dentro de una única transacción de Identity. Aplica los
    /// límites de abuso configurados por usuario.
    /// </summary>
    Task<Result<PasswordResetTokenResult>> GeneratePasswordResetAsync(
        string userId,
        IReadOnlyCollection<string> allowedRoles,
        CancellationToken ct = default
    );

    /// <summary>
    /// Verifica el token contra un usuario y propósito conocidos y, si es
    /// válido, lo consume atómicamente (un solo uso).
    /// </summary>
    Task<AccountTokenVerificationResult> VerifyAndConsumeAsync(
        string userId,
        AccountTokenType type,
        string token,
        CancellationToken ct = default
    );

    /// <summary>
    /// Verifica el token por su valor (sin userId previo) y, si es válido, lo
    /// consume atómicamente (un solo uso). Devuelve el usuario asociado.
    /// </summary>
    Task<TokenVerification> VerifyAndConsumeByTokenAsync(
        AccountTokenType type,
        string token,
        CancellationToken ct = default
    );

    /// <summary>
    /// Consumes an activation token and activates its user in one Identity
    /// transaction. A valid token must belong to an inactive user.
    /// </summary>
    Task<Result<AccountTokenVerificationResult>> CompleteActivationAsync(
        string token,
        CancellationToken ct = default
    );

    /// <summary>
    /// Completa el restablecimiento de contraseña consumiendo el token, cambiando
    /// la contraseña y reactivando el usuario dentro de una sola transacción de
    /// Identity.
    /// </summary>
    Task<Result<AccountTokenVerificationResult>> CompletePasswordResetAsync(
        string userId,
        string token,
        string newPassword,
        CancellationToken ct = default
    );
}
