using ArtemisBankingPro.Infrastructure.Persistence.Services;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class NumberGeneratorTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    [Fact]
    public async Task NextAccountNumber_ReturnsNineDigitNumber() {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var generator = scope.ServiceProvider.GetRequiredService<NumberGenerator>();

        string number = await generator.NextAccountNumberAsync();

        number.Should().MatchRegex(@"^\d{9}$");
    }

    [Fact]
    public async Task NextCardNumber_ReturnsSixteenDigitsWithValidLuhn() {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var generator = scope.ServiceProvider.GetRequiredService<NumberGenerator>();

        string number = await generator.NextCardNumberAsync();

        number.Should().MatchRegex(@"^\d{16}$");
        IsLuhnValid(number).Should().BeTrue();
    }

    [Fact]
    public async Task ParallelGeneration_AllValuesAreDistinct() {
        var tasks = Enumerable.Range(0, 50)
            .Select(
                _ => Task.Run(async () => {
                    await using var scope = Fixture.Services.CreateAsyncScope();
                    var generator = scope.ServiceProvider.GetRequiredService<NumberGenerator>();
                    return await generator.NextAccountNumberAsync();
                })
            );

        string[] numbers = await Task.WhenAll(tasks);

        numbers.Distinct().Should().HaveCount(50);
    }

    [Fact]
    public async Task ParallelGeneration_AccountsAndLoansShareOneNamespace() {
        var tasks = Enumerable.Range(0, 25)
            .SelectMany(_ => new[]
            {
                Task.Run(async () => {
                    await using var scope = Fixture.Services.CreateAsyncScope();
                    var generator = scope.ServiceProvider.GetRequiredService<NumberGenerator>();
                    return await generator.NextAccountNumberAsync();
                }),
                Task.Run(async () => {
                    await using var scope = Fixture.Services.CreateAsyncScope();
                    var generator = scope.ServiceProvider.GetRequiredService<NumberGenerator>();
                    return await generator.NextLoanNumberAsync();
                }),
            });

        string[] numbers = await Task.WhenAll(tasks);

        numbers.Distinct().Should().HaveCount(50);
    }

    private static bool IsLuhnValid(string number) {
        int sum = 0;
        bool doubleDigit = false;

        for (int index = number.Length - 1; index >= 0; index--) {
            int digit = number[index] - '0';
            if (doubleDigit) {
                digit *= 2;
                if (digit > 9) {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        return sum % 10 == 0;
    }
}
