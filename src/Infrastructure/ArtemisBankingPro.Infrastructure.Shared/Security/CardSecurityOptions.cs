namespace ArtemisBankingPro.Infrastructure.Shared.Security;

/// <summary>
/// Configuración de claves para derivados de tarjeta.
/// </summary>
public sealed class CardSecurityOptions {
    public const string SectionName = "Security:Card";

    /// <summary>Clave HMAC-SHA256 (32 bytes en base64) para la huella del PAN.</summary>
    public string? FingerprintKey { get; set; }

    /// <summary>Clave HMAC-SHA256 (32 bytes en base64) para el digest del CVC.</summary>
    public string? CvcPepperKey { get; set; }
}
