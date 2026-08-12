using ArtemisBankingPro.Application.Features.HermesPay.Commands;
using ArtemisBankingPro.Application.Features.HermesPay.Validators;

namespace ArtemisBankingPro.UnitTests.Application.Features.HermesPay.Validators;

public sealed class ProcessHermesPayCommandValidatorTests {
    private readonly ProcessHermesPayCommandValidator _validator = new();

    private static ProcessHermesPayCommand ValidCommand(decimal amount = 689.25m) =>
        new(5, "1589963258467598", "02", "2028", "859", amount);

    [Fact]
    public async Task Validate_ValidCommand_Passes() {
        var result = await _validator.ValidateAsync(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("158996325846759")]
    [InlineData("15899632584675980")]
    [InlineData("15899632584675AB")]
    [InlineData("1589-9632-5846-7598")]
    public async Task Validate_InvalidCardNumber_Fails(string cardNumber) {
        var result = await _validator.ValidateAsync(
            ValidCommand() with { CardNumber = cardNumber }
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CardNumber");
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("13")]
    [InlineData("00")]
    [InlineData("2")]
    public async Task Validate_InvalidExpirationMonth_Fails(string month) {
        var result = await _validator.ValidateAsync(
            ValidCommand() with { MonthExpirationCard = month }
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "MonthExpirationCard");
    }

    [Theory]
    [InlineData("")]
    [InlineData("28")]
    [InlineData("abcd")]
    [InlineData("999")]
    public async Task Validate_InvalidExpirationYear_Fails(string year) {
        var result = await _validator.ValidateAsync(
            ValidCommand() with { YearExpirationCard = year }
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "YearExpirationCard");
    }

    [Theory]
    [InlineData("")]
    [InlineData("85")]
    [InlineData("8591")]
    [InlineData("85a")]
    public async Task Validate_InvalidCvc_Fails(string cvc) {
        var result = await _validator.ValidateAsync(
            ValidCommand() with { Cvc = cvc }
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Cvc");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Validate_NonPositiveAmount_Fails(decimal amount) {
        var result = await _validator.ValidateAsync(ValidCommand(amount));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TransactionAmount");
    }
}
