namespace ArtemisBankingPro.Application.Interfaces.Identity;

public enum AccountTokenType
{
    Activation = 1,
    PasswordReset = 2,
}

public enum AccountTokenVerificationResult
{
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
public sealed record TokenVerification(AccountTokenVerificationResult Result, string? UserId)
{
    public bool IsValid => Result == AccountTokenVerificationResult.Valid;
}

/// <summary>
/// Generación y verificación de tokens de activación y restablecimiento de
/// contraseña. Los tokens son aleatorios, de un solo uso, con vencimiento y
/// vinculados a usuario y propósito; solo se persiste su hash HMAC con clave.
/// Implementado por Infrastructure.Identity.
/// </summary>
public interface IAccountTokenService
{
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
}
