using ArtemisBankingPro.Application.Features.SavingsAccounts.Commands;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Validators;
using FluentValidation.TestHelper;

namespace ArtemisBankingPro.UnitTests.Application.Features.SavingsAccounts;

public sealed class AssignSecondarySavingsAccountCommandTests {
    [Fact]
    public void Metadata_RequiresAdministratorAndDefinesStableIdempotencyValues() {
        var command = new AssignSecondarySavingsAccountCommand("client-1", 1_500m);

        command.RequiredRoles.Should().Equal("Administrador");
        command.IdempotencyKey.Should().Be("open-secondary-client-1-1500");
        command.RequestFingerprint.Should().Be("client-1|1500");
    }

    [Fact]
    public void Validate_ValidCommand_HasNoErrors() {
        var validator = new AssignSecondarySavingsAccountCommandValidator();

        var result = validator.TestValidate(
            new AssignSecondarySavingsAccountCommand("client-1", 0m)
        );

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyCustomerAndNegativeAmount_HasErrors() {
        var validator = new AssignSecondarySavingsAccountCommandValidator();

        var result = validator.TestValidate(
            new AssignSecondarySavingsAccountCommand(string.Empty, -1m)
        );

        result.ShouldHaveValidationErrorFor(command => command.CustomerUserId);
        result.ShouldHaveValidationErrorFor(command => command.InitialAmount);
    }
}
