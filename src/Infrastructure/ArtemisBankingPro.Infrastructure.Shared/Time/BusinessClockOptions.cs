namespace ArtemisBankingPro.Infrastructure.Shared.Time;

/// <summary>
/// Configuración del reloj de negocio. Zona horaria empresarial única para
/// indicadores diarios, mora y vencimientos.
/// </summary>
public sealed class BusinessClockOptions {
    public const string SectionName = "Time:Business";

    /// <summary>Id IANA de la zona horaria de negocio. Default: America/Santo_Domingo.</summary>
    public string TimeZoneId { get; set; } = "America/Santo_Domingo";
}
