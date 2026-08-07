using ArtemisBankingPro.Application.Interfaces.Time;
using Microsoft.Extensions.Options;

namespace ArtemisBankingPro.Infrastructure.Shared.Time;

/// <summary>
/// Reloj de negocio: "hoy" del banco en la zona horaria empresarial
/// configurada (America/Santo_Domingo), con instantes en UTC como base.
/// </summary>
public sealed class BusinessClock : IBusinessClock
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _businessTimeZone;

    public BusinessClock(TimeProvider timeProvider, IOptions<BusinessClockOptions> options)
    {
        _timeProvider = timeProvider;
        _businessTimeZone = ResolveTimeZone(options.Value.TimeZoneId);
    }

    public TimeZoneInfo BusinessTimeZone => _businessTimeZone;

    public DateTimeOffset NowUtc => _timeProvider.GetUtcNow();

    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(NowUtc, _businessTimeZone);

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public DateTimeOffset ToBusinessTime(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, _businessTimeZone);

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new InvalidOperationException(
                $"La zona horaria '{timeZoneId}' (Time:Business:TimeZoneId) no existe "
                    + "en este sistema. Use un identificador IANA válido.",
                ex
            );
        }
    }
}
