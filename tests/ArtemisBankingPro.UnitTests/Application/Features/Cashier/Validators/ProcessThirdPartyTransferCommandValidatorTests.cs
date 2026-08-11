using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Application.Features.Cashier.Validators;

namespace ArtemisBankingPro.UnitTests.Application.Features.Cashier.Validators;

public sealed class ProcessThirdPartyTransferCommandValidatorTests {
    private readonly ProcessThirdPartyTransferCommandValidator _validator = new();

    private static ProcessThirdPartyTransferCommand ValidCommand(decimal amount = 200m) =>
        new("100000001", "100000002", amount);

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
    [InlineData("")]
    [InlineData("123")]
    [InlineData("1234567890")]
    [InlineData("ABCDEFGHI")]
    public async Task Validate_InvalidSourceNumber_Fails(string number) {
        var result = await _validator.ValidateAsync(
            new ProcessThirdPartyTransferCommand(number, "100000002", 200m)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "SourceAccountNumber");
    }

    [Fact]
    public async Task Validate_InvalidDestinationNumber_Fails() {
        var result = await _validator.ValidateAsync(
            new ProcessThirdPartyTransferCommand("100000001", "ABCDEFGHI", 200m)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DestinationAccountNumber");
    }

    [Fact]
    public async Task Validate_SameSourceAndDestination_Fails() {
        var result = await _validator.ValidateAsync(
            new ProcessThirdPartyTransferCommand("100000001", "100000001", 200m)
        );

        result.IsValid.Should().BeFalse();
        result
            .Errors.Should()
            .Contain(e =>
                e.PropertyName == "DestinationAccountNumber"
                && e.ErrorMessage == "La cuenta destino debe ser diferente a la cuenta origen."
            );
    }
}
