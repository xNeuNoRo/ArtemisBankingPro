using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using ArtemisBankingPro.Application.Features.CreditCard.Validator;
using FluentValidation.TestHelper;

namespace ArtemisBankingPro.UnitTests.Application.Features.CreditCard.Validators;

public sealed class UpdateCardLimitCommandValidatorTests {
    private readonly UpdateCardLimitCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCardAndLimit_HasNoErrors() {
        var result = _validator.TestValidate(new UpdateCardLimitCommand(1, 15_000m));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ZeroLimit_HasError() {
        var result = _validator.TestValidate(new UpdateCardLimitCommand(1, 0m));

        result.ShouldHaveValidationErrorFor(command => command.NewLimit);
    }

    [Fact]
    public void Validate_NegativeLimit_HasError() {
        var result = _validator.TestValidate(new UpdateCardLimitCommand(1, -100m));

        result.ShouldHaveValidationErrorFor(command => command.NewLimit);
    }

    [Fact]
    public void Validate_CardIdZero_HasError() {
        var result = _validator.TestValidate(new UpdateCardLimitCommand(0, 15_000m));

        result.ShouldHaveValidationErrorFor(command => command.CardId);
    }
}
