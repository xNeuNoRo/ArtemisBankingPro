using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Application.Features.Cashier.Validators;

namespace ArtemisBankingPro.UnitTests.Application.Features.Cashier.Validators;

public sealed class ProcessCardPaymentCommandValidatorTests {
    private readonly ProcessCardPaymentCommandValidator _validator = new();

    private static ProcessCardPaymentCommand ValidCommand(decimal amount = 1000m) =>
        new(1, "100000001", amount);

    [Fact]
    public async Task Validate_ValidCommand_Passes() {
        var result = await _validator.ValidateAsync(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Validate_NonPositiveAmount_Fails(decimal amount) {
        var result = await _validator.ValidateAsync(ValidCommand(amount));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Amount");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Validate_InvalidCardId_Fails(int cardId) {
        var result = await _validator.ValidateAsync(
            new ProcessCardPaymentCommand(cardId, "100000001", 1000m)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CardId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("1234567890")]
    [InlineData("ABCDEFGHI")]
    public async Task Validate_InvalidAccountNumber_Fails(string accountNumber) {
        var result = await _validator.ValidateAsync(
            new ProcessCardPaymentCommand(1, accountNumber, 1000m)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "AccountNumber");
    }
}
