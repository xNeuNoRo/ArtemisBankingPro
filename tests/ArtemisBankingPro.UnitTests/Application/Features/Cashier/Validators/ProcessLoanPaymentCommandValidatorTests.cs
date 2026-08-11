using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Application.Features.Cashier.Validators;

namespace ArtemisBankingPro.UnitTests.Application.Features.Cashier.Validators;

public sealed class ProcessLoanPaymentCommandValidatorTests {
    private readonly ProcessLoanPaymentCommandValidator _validator = new();

    private static ProcessLoanPaymentCommand ValidCommand(decimal amount = 1000m) =>
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
    public async Task Validate_InvalidLoanId_Fails(int loanId) {
        var result = await _validator.ValidateAsync(
            new ProcessLoanPaymentCommand(loanId, "100000001", 1000m)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "LoanId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("1234567890")]
    [InlineData("ABCDEFGHI")]
    public async Task Validate_InvalidAccountNumber_Fails(string accountNumber) {
        var result = await _validator.ValidateAsync(
            new ProcessLoanPaymentCommand(1, accountNumber, 1000m)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "AccountNumber");
    }
}
