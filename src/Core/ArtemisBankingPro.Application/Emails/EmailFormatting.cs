using System.Globalization;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Emails;

/// <summary>
/// Formateo invariante para correos, dinero en RD$, tasas y fechas en la zona
/// de negocio.
/// </summary>
public static class EmailFormatting
{
    public static string FormatMoney(Money money) =>
        $"RD$ {money.Amount.ToString("0.00", CultureInfo.InvariantCulture)}";

    public static string FormatAnnualRate(decimal annualPercentage) =>
        annualPercentage.ToString("0.##", CultureInfo.InvariantCulture);

    public static string FormatDateTime(DateTimeOffset value, TimeZoneInfo timeZone) =>
        TimeZoneInfo
            .ConvertTime(value, timeZone)
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string FormatDate(DateOnly value) =>
        value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
