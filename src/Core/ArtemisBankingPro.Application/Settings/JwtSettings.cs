namespace ArtemisBankingPro.Application.Settings;

/// <summary>
/// Configuración de JWT: firma HMAC, emisor, audiencia y vigencia.
/// La clave secreta nunca se commitea; se provee por configuración segura.
/// </summary>
public sealed class JwtSettings {
    public const string SectionName = "Security:Jwt";

    /// <summary>Clave HMAC de firma en base64 (mínimo 32 bytes).</summary>
    public string? SecretKey { get; set; }

    public string? Issuer { get; set; }

    public string? Audience { get; set; }

    public int ExpirationMinutes { get; set; } = 15;
}
