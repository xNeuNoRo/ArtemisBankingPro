using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Common.Validation;
using ArtemisBankingPro.Application.Features.Users.Queries;
using ArtemisBankingPro.Application.Features.Users.Validators;

namespace ArtemisBankingPro.UnitTests.Application.Features.Users.Validators;

public sealed class CreateUserCommandValidatorTests {
    private readonly CreateUserCommandValidator _validator = new();

    private static CreateUserCommand ValidClient() =>
        new(
            "María",
            "Gómez",
            "00187654321",
            "maria@artemis.com",
            "maria01",
            "123P@$$word!",
            "123P@$$word!",
            "Cliente",
            InitialAmount: 5000m
        );

    [Fact]
    public async Task Validate_ValidClient_Passes() {
        var result = await _validator.ValidateAsync(ValidClient());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyFirstName_Fails() {
        var result = await _validator.ValidateAsync(ValidClient() with { FirstName = "" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FirstName");
    }

    [Fact]
    public async Task Validate_InvalidEmail_Fails() {
        var result = await _validator.ValidateAsync(ValidClient() with { Email = "not-an-email" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public async Task Validate_IdentificationLongerThanSqlColumn_Fails() {
        var result = await _validator.ValidateAsync(
            ValidClient() with { Identification = "123456789012" }
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Identification"
            && e.ErrorMessage == IdentityValidationLimits.IdentificationMaxLengthMessage
        );
    }

    [Fact]
    public async Task Validate_PasswordMismatch_Fails() {
        var result = await _validator.ValidateAsync(
            ValidClient() with { ConfirmPassword = "different" }
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConfirmPassword");
    }

    [Fact]
    public async Task Validate_InvalidRole_Fails() {
        var result = await _validator.ValidateAsync(ValidClient() with { Role = "Comercio" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Role");
    }

    [Fact]
    public async Task Validate_NegativeInitialAmountForClient_Fails() {
        var result = await _validator.ValidateAsync(ValidClient() with { InitialAmount = -1m });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "InitialAmount");
    }

    [Fact]
    public async Task Validate_InitialAmountForAdmin_Fails() {
        var result = await _validator.ValidateAsync(
            ValidClient() with { Role = "Administrador", InitialAmount = 100m }
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "InitialAmount");
    }

    [Fact]
    public async Task Validate_InvalidCallbackUrl_Fails() {
        var result = await _validator.ValidateAsync(ValidClient() with { CallbackUrl = "bad" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CallbackUrl");
    }
}

public sealed class UpdateUserCommandValidatorTests {
    private readonly UpdateUserCommandValidator _validator = new();

    private static UpdateUserCommand ValidUpdate() =>
        new(
            "user-1",
            "María",
            "Gómez",
            "00187654321",
            "maria@artemis.com",
            "maria01"
        );

    [Fact]
    public async Task Validate_ValidUpdate_Passes() {
        var result = await _validator.ValidateAsync(ValidUpdate());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyUserId_Fails() {
        var result = await _validator.ValidateAsync(ValidUpdate() with { UserId = "" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "UserId");
    }

    [Fact]
    public async Task Validate_PasswordWithoutConfirmation_Fails() {
        var result = await _validator.ValidateAsync(ValidUpdate() with { Password = "NuevaP@ss1" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConfirmPassword");
    }

    [Fact]
    public async Task Validate_PasswordWithMismatch_Fails() {
        var result = await _validator.ValidateAsync(
            ValidUpdate() with { Password = "NuevaP@ss1", ConfirmPassword = "OtraP@ss1" }
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConfirmPassword");
    }

    [Fact]
    public async Task Validate_PasswordWithMatch_Passes() {
        var result = await _validator.ValidateAsync(
            ValidUpdate() with { Password = "NuevaP@ss1", ConfirmPassword = "NuevaP@ss1" }
        );

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_NegativeAdditionalAmount_Fails() {
        var result = await _validator.ValidateAsync(ValidUpdate() with { AdditionalAmount = -5m });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "AdditionalAmount");
    }

    [Fact]
    public async Task Validate_ZeroAdditionalAmount_Passes() {
        var result = await _validator.ValidateAsync(ValidUpdate() with { AdditionalAmount = 0m });

        result.IsValid.Should().BeTrue();
    }
}

public sealed class ChangeUserStatusCommandValidatorTests {
    private readonly ChangeUserStatusCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ValidCommand_Passes() {
        var result = await _validator.ValidateAsync(new ChangeUserStatusCommand("user-1", true));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyUserId_Fails() {
        var result = await _validator.ValidateAsync(new ChangeUserStatusCommand("", true));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "UserId");
    }
}

public sealed class CreateCommerceUserCommandValidatorTests {
    private readonly CreateCommerceUserCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ValidCommand_Passes() {
        var result = await _validator.ValidateAsync(
            new CreateCommerceUserCommand(
                5,
                "Comercio",
                "Demo",
                "10199999999",
                "comercio@demo.com",
                "comercio01",
                "123P@$$word!",
                "123P@$$word!",
                0m
            )
        );

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ZeroCommerceId_Fails() {
        var result = await _validator.ValidateAsync(
            new CreateCommerceUserCommand(
                0,
                "Comercio",
                "Demo",
                "10199999999",
                "comercio@demo.com",
                "comercio01",
                "123P@$$word!",
                "123P@$$word!"
            )
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CommerceId");
    }

    [Fact]
    public async Task Validate_NegativeInitialAmount_Fails() {
        var result = await _validator.ValidateAsync(
            new CreateCommerceUserCommand(
                5,
                "Comercio",
                "Demo",
                "10199999999",
                "comercio@demo.com",
                "comercio01",
                "123P@$$word!",
                "123P@$$word!",
                -1m
            )
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "InitialAmount");
    }

    [Fact]
    public async Task Validate_MissingInitialAmount_Fails() {
        var result = await _validator.ValidateAsync(
            new CreateCommerceUserCommand(
                5,
                "Comercio",
                "Demo",
                "10199999999",
                "comercio@demo.com",
                "comercio01",
                "123P@$$word!",
                "123P@$$word!"
            )
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "InitialAmount");
    }
}

public sealed class UserQueryValidatorTests {
    private readonly GetUsersPagedQueryValidator _pagedValidator = new();
    private readonly GetCommerceUsersPagedQueryValidator _commerceValidator = new();
    private readonly GetUserByIdQueryValidator _byIdValidator = new();

    [Fact]
    public async Task Validate_PagedDefaults_Pass() {
        var result = await _pagedValidator.ValidateAsync(new GetUsersPagedQuery());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_PageZero_Fails() {
        var result = await _pagedValidator.ValidateAsync(new GetUsersPagedQuery(Page: 0));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }

    [Fact]
    public async Task Validate_PageSizeOverMax_Fails() {
        var result = await _pagedValidator.ValidateAsync(new GetUsersPagedQuery(PageSize: 50));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PageSize");
    }

    [Fact]
    public async Task Validate_InvalidRoleFilter_Fails() {
        var result = await _pagedValidator.ValidateAsync(new GetUsersPagedQuery(Role: "Comercio"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Role");
    }

    [Fact]
    public async Task Validate_CommercePaged_Passes() {
        var result = await _commerceValidator.ValidateAsync(new GetCommerceUsersPagedQuery());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ById_Empty_Fails() {
        var result = await _byIdValidator.ValidateAsync(new GetUserByIdQuery(""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "UserId");
    }
}
