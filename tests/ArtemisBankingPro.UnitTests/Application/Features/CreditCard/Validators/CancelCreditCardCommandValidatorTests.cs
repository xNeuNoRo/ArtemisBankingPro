using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using ArtemisBankingPro.Application.Features.CreditCard.Validator;
using FluentValidation.TestHelper;

namespace ArtemisBankingPro.UnitTests.Application.Features.CreditCard.Validators;

public sealed class CancelCreditCardCommandValidatorTests {
    private readonly CancelCreditCardCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCardId_HasNoErrors() {
        var result = _validator.TestValidate(new CancelCreditCardCommand(1));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_CardIdZero_HasError() {
        var result = _validator.TestValidate(new CancelCreditCardCommand(0));

        result.ShouldHaveValidationErrorFor(command => command.CardId);
    }

    [Fact]
    public void Validate_NegativeCardId_HasError() {
        var result = _validator.TestValidate(new CancelCreditCardCommand(-5));

        result.ShouldHaveValidationErrorFor(command => command.CardId);
    }
}
