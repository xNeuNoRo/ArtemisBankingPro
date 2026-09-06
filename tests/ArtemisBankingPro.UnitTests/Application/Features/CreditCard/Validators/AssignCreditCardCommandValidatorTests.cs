using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using ArtemisBankingPro.Application.Features.CreditCard.Validator;
using FluentValidation.TestHelper;

namespace ArtemisBankingPro.UnitTests.Application.Features.CreditCard.Validators;

public sealed class AssignCreditCardCommandValidatorTests {
    private readonly AssignCreditCardCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidClientAndLimit_HasNoErrors() {
        var result = _validator.TestValidate(new AssignCreditCardCommand("client-1", 15_000m));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyCustomer_HasError() {
        var result = _validator.TestValidate(new AssignCreditCardCommand(string.Empty, 15_000m));

        result.ShouldHaveValidationErrorFor(command => command.CustomerUserId);
    }

    [Fact]
    public void Validate_WhitespaceCustomer_HasError() {
        var result = _validator.TestValidate(new AssignCreditCardCommand("   ", 15_000m));

        result.ShouldHaveValidationErrorFor(command => command.CustomerUserId);
    }

    [Fact]
    public void Validate_OverLongCustomer_HasError() {
        var result = _validator.TestValidate(
            new AssignCreditCardCommand(new string('x', 451), 15_000m)
        );

        result.ShouldHaveValidationErrorFor(command => command.CustomerUserId);
    }

    [Fact]
    public void Validate_ZeroLimit_HasError() {
        var result = _validator.TestValidate(new AssignCreditCardCommand("client-1", 0m));

        result.ShouldHaveValidationErrorFor(command => command.CreditLimit);
    }

    [Fact]
    public void Validate_NegativeLimit_HasError() {
        var result = _validator.TestValidate(new AssignCreditCardCommand("client-1", -100m));

        result.ShouldHaveValidationErrorFor(command => command.CreditLimit);
    }
}
