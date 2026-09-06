namespace ArtemisBankingPro.Application.Common.Time;

/// <summary>
/// Normaliza fechas introducidas como días de negocio a instantes UTC. Los
/// filtros de historial usan límites inclusivos para incluir el día completo.
/// </summary>
public static class BusinessDateRange {
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) ToUtcRange(
        TimeZoneInfo businessTimeZone,
        DateOnly date
    ) {
        ArgumentNullException.ThrowIfNull(businessTimeZone);

        DateTime startLocal = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        DateTime endLocal = startLocal.AddDays(1);
        return (
            ToUtc(businessTimeZone, startLocal),
            ToUtc(businessTimeZone, endLocal)
        );
    }

    public static (DateTimeOffset? DateFrom, DateTimeOffset? DateTo) NormalizeInclusive(
        TimeZoneInfo businessTimeZone,
        DateTimeOffset? dateFrom,
        DateTimeOffset? dateTo
    ) => (
        dateFrom is null ? null : ToUtc(businessTimeZone, BusinessDate(dateFrom.Value)),
        dateTo is null
            ? null
            : ToUtc(
                businessTimeZone,
                BusinessDate(dateTo.Value).AddDays(1).AddTicks(-1)
            )
    );

    private static DateTime BusinessDate(DateTimeOffset value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);

    private static DateTimeOffset ToUtc(TimeZoneInfo businessTimeZone, DateTime localTime) =>
        new DateTimeOffset(localTime, businessTimeZone.GetUtcOffset(localTime)).ToUniversalTime();
}
