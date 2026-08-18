using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Application.Features.Cashier.Validators;

namespace ArtemisBankingPro.UnitTests.Application.Features.Cashier.Validators;

public sealed class ProcessWithdrawalCommandValidatorTests {
    private readonly ProcessWithdrawalCommandValidator _validator = new();

    private static ProcessWithdrawalCommand ValidCommand(decimal amount = 1000m) =>
        new("100000001", amount, "test-key");

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
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Amount"
            && e.ErrorMessage == "El monto a retirar debe ser mayor que cero."
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("1234567890")]
    [InlineData("ABCDEFGHI")]
    public async Task Validate_InvalidAccountNumber_Fails(string accountNumber) {
        var result = await _validator.ValidateAsync(
            new ProcessWithdrawalCommand(accountNumber, 1000m, "test-key")
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "AccountNumber");
    }
}
