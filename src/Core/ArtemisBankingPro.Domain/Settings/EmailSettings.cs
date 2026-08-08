namespace ArtemisBankingPro.Domain.Settings;

/// <summary>
/// Configuración SMTP para el envío de correos. Las credenciales nunca se
/// commitean; se proveen por user secrets, variables de entorno o
/// appsettings.Development.json.
/// </summary>
public sealed class EmailSettings {
    public const string SectionName = "Email:Smtp";

    /// <summary>Host del servidor SMTP (obligatorio para envío).</summary>
    public string? Host { get; init; }

    /// <summary>Puerto SMTP. Default 587.</summary>
    public int Port { get; init; } = 587;

    /// <summary>Indica si la conexión SMTP debe usar TLS.</summary>
    public bool EnableSsl { get; init; } = true;

    /// <summary>Usuario de autenticación SMTP (opcional si el servidor no exige).</summary>
    public string? UserName { get; init; }

    /// <summary>Contraseña de autenticación SMTP (opcional si el servidor no exige).</summary>
    public string? Password { get; init; }

    /// <summary>Dirección remitente (obligatoria para envío).</summary>
    public string? FromAddress { get; init; }

    /// <summary>Nombre visible del remitente.</summary>
    public string? FromName { get; init; }

    /// <summary>Tiempo máximo de espera por operación SMTP, en segundos.</summary>
    public int TimeoutSeconds { get; init; } = 15;

    /// <summary>
    /// Verifica si la configuración es suficiente para intentar un envío.
    /// Las credenciales son opcionales (hay servidores SMTP sin autenticación).
    /// </summary>
    public bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(Host) && Port > 0 && !string.IsNullOrWhiteSpace(FromAddress);
}
