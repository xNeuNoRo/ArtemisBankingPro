namespace ArtemisBankingPro.Infrastructure.Identity.Security;

/// <summary>
/// Configuración del key ring usado por cookies Identity y antiforgery.
/// </summary>
public sealed class DataProtectionOptions {
    public const string SectionName = "Security:DataProtection";

    public string ApplicationName { get; set; } = "ArtemisBankingPro.WebApp";

    /// <summary>
    /// Ruta compartida y persistente en despliegues no efímeros o multi-instancia.
    /// </summary>
    public string? KeyRingPath { get; set; }
}
