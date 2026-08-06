using System.Globalization;
using ArtemisBankingPro.Domain.Cards.Errors;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Cards.ValueObjects;

public sealed record CardExpiration {
    private CardExpiration(int month, int year) {
        Month = month;
        Year = year;
    }

    public int Month { get; }

    public int Year { get; }

    public static Result<CardExpiration> Create(int month, int year) {
        if (month is < 1 or > 12 || year is < 1 or > 9999) {
            return Result.Failure<CardExpiration>(CardErrors.InvalidExpiration);
        }

        return Result.Success(new CardExpiration(month, year));
    }

    public static CardExpiration FromIssueDate(DateOnly issueDate) =>
        new(issueDate.Month, issueDate.Year + 3);

    public bool IsExpired(DateOnly businessDate) =>
        businessDate > new DateOnly(Year, Month, DateTime.DaysInMonth(Year, Month));

    public bool Matches(int month, int year) => Month == month && Year == NormalizeYear(year);

    public override string ToString() =>
        $"{Month.ToString("00", CultureInfo.InvariantCulture)}/{(Year % 100).ToString("00", CultureInfo.InvariantCulture)}";

    private static int NormalizeYear(int year) => year is >= 0 and <= 99 ? 2000 + year : year;
}
