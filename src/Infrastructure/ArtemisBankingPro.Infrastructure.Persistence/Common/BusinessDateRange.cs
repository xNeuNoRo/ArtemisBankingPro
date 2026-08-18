namespace ArtemisBankingPro.Infrastructure.Persistence.Common;

/// <summary>
/// Convierte una fecha de negocio a un rango [inicio, fin) en UTC usando la
/// zona horaria empresarial configurada. Lo usan los repositorios de
/// lectura para filtrar operaciones del día (dashboard de cajero y
/// dashboard administrativo).
/// </summary>
public static class BusinessDateRange {
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) ToUtcRange(
        TimeZoneInfo timeZone,
        DateOnly date
    ) {
        DateTime startLocal = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        DateTime endLocal = startLocal.AddDays(1);

        DateTimeOffset startUtc = new DateTimeOffset(startLocal, timeZone.GetUtcOffset(startLocal))
            .ToUniversalTime();
        DateTimeOffset endUtc = new DateTimeOffset(endLocal, timeZone.GetUtcOffset(endLocal))
            .ToUniversalTime();

        return (startUtc, endUtc);
    }
}
