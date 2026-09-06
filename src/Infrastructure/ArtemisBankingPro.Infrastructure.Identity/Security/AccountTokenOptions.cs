namespace ArtemisBankingPro.Infrastructure.Identity.Security;

/// <summary>
/// Configuración de tokens de cuenta: pepper para el hash HMAC y vigencias.
/// El pepper nunca se commitea; se provee por configuración segura.
/// </summary>
public sealed class AccountTokenOptions {
    public const string SectionName = "Security:AccountTokens";

    /// <summary>Clave HMAC-SHA256 (32 bytes en base64) para el hash de tokens.</summary>
    public string? PepperKey { get; set; }

    public int ActivationLifetimeMinutes { get; set; } = 24 * 60;

    public int ResetLifetimeMinutes { get; set; } = 30;

    /// <summary>Tiempo mínimo entre solicitudes de reset del mismo usuario.</summary>
    public int ResetRequestCooldownSeconds { get; set; } = 60;

    /// <summary>Ventana usada para limitar la generación de resets.</summary>
    public int ResetRequestWindowMinutes { get; set; } = 15;

    /// <summary>Máximo de solicitudes dentro de la ventana configurada.</summary>
    public int MaxResetRequestsPerWindow { get; set; } = 3;
}
