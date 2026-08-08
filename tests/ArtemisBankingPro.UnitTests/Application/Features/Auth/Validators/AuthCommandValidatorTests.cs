using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.Validators;

namespace ArtemisBankingPro.UnitTests.Application.Features.Auth.Validators;

public sealed class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ValidCommand_Passes()
    {
        var result = await _validator.ValidateAsync(new LoginCommand("admin", "123P@$$word!"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyUserName_Fails()
    {
        var result = await _validator.ValidateAsync(new LoginCommand("", "123P@$$word!"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "UserName");
    }

    [Fact]
    public async Task Validate_EmptyPassword_Fails()
    {
        var result = await _validator.ValidateAsync(new LoginCommand("admin", ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }
}

public sealed class ActivateAccountCommandValidatorTests
{
    private readonly ActivateAccountCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ValidToken_Passes()
    {
        var result = await _validator.ValidateAsync(new ActivateAccountCommand("token-123"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyToken_Fails()
    {
        var result = await _validator.ValidateAsync(new ActivateAccountCommand(""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Token");
    }
}

public sealed class RequestPasswordResetCommandValidatorTests
{
    private readonly RequestPasswordResetCommandValidator _validator = new();

    private static readonly string[] MvcRoles = ["Administrador", "Cajero", "Cliente"];

    [Fact]
    public async Task Validate_ValidCommand_Passes()
    {
        var result = await _validator.ValidateAsync(
            new RequestPasswordResetCommand("admin", MvcRoles)
        );

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyUserName_Fails()
    {
        var result = await _validator.ValidateAsync(
            new RequestPasswordResetCommand("", MvcRoles)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "UserName");
    }

    [Fact]
    public async Task Validate_EmptyAllowedRoles_Fails()
    {
        var result = await _validator.ValidateAsync(
            new RequestPasswordResetCommand("admin", [])
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "AllowedRoles");
    }

    [Fact]
    public async Task Validate_InvalidCallbackUrl_Fails()
    {
        var result = await _validator.ValidateAsync(
            new RequestPasswordResetCommand("admin", MvcRoles, "not-a-url")
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CallbackUrl");
    }

    [Fact]
    public async Task Validate_ValidCallbackUrl_Passes()
    {
        var result = await _validator.ValidateAsync(
            new RequestPasswordResetCommand("admin", MvcRoles, "https://artemis.local")
        );

        result.IsValid.Should().BeTrue();
    }
}

public sealed class ResetPasswordCommandValidatorTests
{
    private readonly ResetPasswordCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ValidCommand_Passes()
    {
        var result = await _validator.ValidateAsync(
            new ResetPasswordCommand("user-1", "token", "123P@$$word!", "123P@$$word!")
        );

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyUserId_Fails()
    {
        var result = await _validator.ValidateAsync(
            new ResetPasswordCommand("", "token", "123P@$$word!", "123P@$$word!")
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "UserId");
    }

    [Fact]
    public async Task Validate_EmptyToken_Fails()
    {
        var result = await _validator.ValidateAsync(
            new ResetPasswordCommand("user-1", "", "123P@$$word!", "123P@$$word!")
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Token");
    }

    [Fact]
    public async Task Validate_ShortPassword_Fails()
    {
        var result = await _validator.ValidateAsync(
            new ResetPasswordCommand("user-1", "token", "123", "123")
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    [Fact]
    public async Task Validate_PasswordMismatch_Fails()
    {
        var result = await _validator.ValidateAsync(
            new ResetPasswordCommand("user-1", "token", "123P@$$word!", "different")
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConfirmPassword");
    }
}
