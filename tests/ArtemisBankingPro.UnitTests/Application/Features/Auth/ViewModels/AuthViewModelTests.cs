using System.ComponentModel.DataAnnotations;
using ArtemisBankingPro.Application.Common.Mapping;
using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.Mapping;
using ArtemisBankingPro.Application.Features.Auth.ViewModels;
using MapsterMapper;

namespace ArtemisBankingPro.UnitTests.Application.Features.Auth.ViewModels;

public sealed class AuthViewModelValidationTests {
    [Fact]
    public void Login_RequiresUsernameAndPassword() {
        var model = new LoginViewModel();

        var errors = Validate(model);

        errors.Should().Contain(error => error.MemberNames.Contains(nameof(LoginViewModel.UserName)));
        errors.Should().Contain(error => error.MemberNames.Contains(nameof(LoginViewModel.Password)));
    }

    [Fact]
    public void ResetPassword_RequiresMatchingPasswords() {
        var model = new ResetPasswordViewModel {
            UserId = "user-1",
            Token = "token-1",
            Password = "123P@$$word!",
            ConfirmPassword = "different",
        };

        var errors = Validate(model);

        errors.Should().Contain(error =>
            error.MemberNames.Contains(nameof(ResetPasswordViewModel.ConfirmPassword))
        );
    }

    [Fact]
    public void ResetPassword_RequiresMinimumPasswordLength() {
        var model = new ResetPasswordViewModel {
            UserId = "user-1",
            Token = "token-1",
            Password = "short",
            ConfirmPassword = "short",
        };

        var errors = Validate(model);

        errors.Should().Contain(error =>
            error.MemberNames.Contains(nameof(ResetPasswordViewModel.Password))
        );
    }

    [Fact]
    public void Activation_RequiresToken() {
        var errors = Validate(new ActivateAccountViewModel());

        errors.Should().Contain(error =>
            error.MemberNames.Contains(nameof(ActivateAccountViewModel.Token))
        );
    }

    private static List<ValidationResult> Validate(object model) {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            results,
            validateAllProperties: true
        );
        return results;
    }
}

public sealed class AuthViewModelMappingTests {
    private static readonly ServiceMapper Mapper = new(null!, MapsterConfig.Create());

    [Fact]
    public void Login_MapsOnlyCredentialFieldsToWebCommand() {
        var viewModel = new LoginViewModel {
            UserName = "admin",
            Password = "123P@$$word!",
        };

        WebAppLoginCommand command = Mapper.Map<WebAppLoginCommand>(viewModel);

        command.UserName.Should().Be("admin");
        command.Password.Should().Be("123P@$$word!");
        typeof(WebAppLoginCommand).GetProperties().Select(property => property.Name)
            .Should().NotContain("Jwt");
    }

    [Fact]
    public void ResetPassword_MapsTokenAndPasswordFields() {
        var viewModel = new ResetPasswordViewModel {
            UserId = "user-1",
            Token = "token-1",
            Password = "123P@$$word!",
            ConfirmPassword = "123P@$$word!",
        };

        ResetPasswordCommand command = Mapper.Map<ResetPasswordCommand>(viewModel);

        command.UserId.Should().Be("user-1");
        command.Token.Should().Be("token-1");
        command.Password.Should().Be("123P@$$word!");
        command.ConfirmPassword.Should().Be("123P@$$word!");
    }

    [Fact]
    public void Activation_MapsTokenOnlyToActivationCommand() {
        var viewModel = new ActivateAccountViewModel { Token = "activation-token" };

        ActivateAccountCommand command = Mapper.Map<ActivateAccountCommand>(viewModel);

        command.Token.Should().Be("activation-token");
        typeof(ActivateAccountCommand).GetProperties().Select(property => property.Name)
            .Should().ContainSingle().Which.Should().Be(nameof(ActivateAccountCommand.Token));
    }

    [Fact]
    public void PasswordResetRequest_UsesServerOwnedRolesAndCallback() {
        var viewModel = new RequestPasswordResetViewModel { UserName = "admin" };
        string[] allowedRoles = ["Administrador", "Cajero", "Cliente"];

        RequestPasswordResetCommand command = viewModel.ToRequestPasswordResetCommand(
            allowedRoles,
            "https://artemis.local"
        );

        command.UserName.Should().Be("admin");
        command.AllowedRoles.Should().Equal(allowedRoles);
        command.CallbackUrl.Should().Be("https://artemis.local");
        typeof(RequestPasswordResetViewModel).GetProperties().Select(property => property.Name)
            .Should().NotContain("AllowedRoles");
        typeof(RequestPasswordResetViewModel).GetProperties().Select(property => property.Name)
            .Should().NotContain("CallbackUrl");
    }

    [Fact]
    public void AccessDenied_DoesNotExposeTokensOrRedirectUrls() {
        typeof(AccessDeniedViewModel).GetProperties().Select(property => property.Name)
            .Should().NotContain(propertyName =>
                propertyName.Contains("Token", StringComparison.OrdinalIgnoreCase)
                || propertyName.Contains("Url", StringComparison.OrdinalIgnoreCase)
                || propertyName.Contains("Jwt", StringComparison.OrdinalIgnoreCase));
    }
}
