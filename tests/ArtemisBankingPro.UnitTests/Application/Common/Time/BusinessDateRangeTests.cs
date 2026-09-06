using ArtemisBankingPro.Application.Common.Time;

namespace ArtemisBankingPro.UnitTests.Application.Common.Time;

public sealed class BusinessDateRangeTests {
    [Fact]
    public void NormalizeInclusive_uses_business_midnight_and_includes_the_selected_end_date() {
        TimeZoneInfo businessZone = TimeZoneInfo.CreateCustomTimeZone(
            "ArtemisBusiness",
            TimeSpan.FromHours(-4),
            "Artemis Business",
            "Artemis Business"
        );

        (DateTimeOffset? from, DateTimeOffset? to) = BusinessDateRange.NormalizeInclusive(
            businessZone,
            new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero)
        );

        from.Should().Be(new DateTimeOffset(2026, 8, 11, 4, 0, 0, TimeSpan.Zero));
        to.Should().Be(new DateTimeOffset(2026, 8, 12, 3, 59, 59, 999, TimeSpan.Zero).AddTicks(9999));
    }
}
