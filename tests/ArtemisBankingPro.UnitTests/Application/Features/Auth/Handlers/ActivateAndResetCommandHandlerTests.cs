using Moq;
using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.Handlers;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.UnitTests.Application.Features.Auth.Handlers;

public sealed class ActivateAccountCommandHandlerTests
{
    private static Mock<IAccountTokenService> TokenService(TokenVerification verification)
    {
        var service = new Mock<IAccountTokenService>();
        service
            .Setup(s => s.VerifyAndConsumeByTokenAsync(AccountTokenType.Activation, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(verification);
        return service;
    }

    [Fact]
    public async Task Handle_ValidToken_ActivatesUser()
    {
        var tokenService = TokenService(
            new TokenVerification(AccountTokenVerificationResult.Valid, "user-1")
        );
        var userService = new Mock<IUserAccountService>();
        userService
            .Setup(s => s.SetActiveAsync("user-1", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var handler = new ActivateAccountCommandHandler(tokenService.Object, userService.Object);

        var result = await handler.Handle(
            new ActivateAccountCommand("token-123"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        userService.Verify(
            s => s.SetActiveAsync("user-1", true, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_InvalidToken_ReturnsValidationErrorAndDoesNotActivate()
    {
        var tokenService = TokenService(
            new TokenVerification(AccountTokenVerificationResult.Invalid, null)
        );
        var userService = new Mock<IUserAccountService>();

        var handler = new ActivateAccountCommandHandler(tokenService.Object, userService.Object);

        var result = await handler.Handle(
            new ActivateAccountCommand("bad-token"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Auth.ActivationInvalid");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
        userService.Verify(
            s => s.SetActiveAsync(It.IsAny<string>(), true, It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_ExpiredToken_ReturnsExpirationMessage()
    {
        var handler = new ActivateAccountCommandHandler(
            TokenService(new TokenVerification(AccountTokenVerificationResult.Expired, null)).Object,
            new Mock<IUserAccountService>().Object
        );

        var result = await handler.Handle(
            new ActivateAccountCommand("expired"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Auth.ActivationExpired");
    }

    [Fact]
    public async Task Handle_AlreadyUsedToken_ReturnsUsedMessage()
    {
        var handler = new ActivateAccountCommandHandler(
            TokenService(new TokenVerification(AccountTokenVerificationResult.AlreadyUsed, null)).Object,
            new Mock<IUserAccountService>().Object
        );

        var result = await handler.Handle(
            new ActivateAccountCommand("used"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Auth.ActivationAlreadyUsed");
    }
}

public sealed class ResetPasswordCommandHandlerTests
{
    private static Mock<IAccountTokenService> TokenService(AccountTokenVerificationResult result)
    {
        var service = new Mock<IAccountTokenService>();
        service
            .Setup(s => s.VerifyAndConsumeAsync(
                It.IsAny<string>(),
                AccountTokenType.PasswordReset,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(result);
        return service;
    }

    [Fact]
    public async Task Handle_ValidToken_ChangesPasswordAndReactivates()
    {
        var tokenService = TokenService(AccountTokenVerificationResult.Valid);
        var userService = new Mock<IUserAccountService>();
        userService
            .Setup(s => s.ChangePasswordAsync("user-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        userService
            .Setup(s => s.SetActiveAsync("user-1", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var handler = new ResetPasswordCommandHandler(tokenService.Object, userService.Object);

        var result = await handler.Handle(
            new ResetPasswordCommand("user-1", "token", "123P@$$word!", "123P@$$word!"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        userService.Verify(
            s => s.ChangePasswordAsync("user-1", "123P@$$word!", It.IsAny<CancellationToken>()),
            Times.Once
        );
        userService.Verify(
            s => s.SetActiveAsync("user-1", true, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_ExpiredToken_ReturnsExpirationMessage()
    {
        var handler = new ResetPasswordCommandHandler(
            TokenService(AccountTokenVerificationResult.Expired).Object,
            new Mock<IUserAccountService>().Object
        );

        var result = await handler.Handle(
            new ResetPasswordCommand("user-1", "expired", "123P@$$word!", "123P@$$word!"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Auth.ResetExpired");
    }

    [Fact]
    public async Task Handle_AlreadyUsedToken_ReturnsUsedMessage()
    {
        var handler = new ResetPasswordCommandHandler(
            TokenService(AccountTokenVerificationResult.AlreadyUsed).Object,
            new Mock<IUserAccountService>().Object
        );

        var result = await handler.Handle(
            new ResetPasswordCommand("user-1", "used", "123P@$$word!", "123P@$$word!"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Auth.ResetAlreadyUsed");
    }

    [Fact]
    public async Task Handle_InvalidToken_ReturnsInvalidMessage()
    {
        var handler = new ResetPasswordCommandHandler(
            TokenService(AccountTokenVerificationResult.Invalid).Object,
            new Mock<IUserAccountService>().Object
        );

        var result = await handler.Handle(
            new ResetPasswordCommand("user-1", "invalid", "123P@$$word!", "123P@$$word!"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Auth.ResetInvalid");
    }

    [Fact]
    public async Task Handle_PasswordChangeFailure_PropagatesError()
    {
        var tokenService = TokenService(AccountTokenVerificationResult.Valid);
        var userService = new Mock<IUserAccountService>();
        userService
            .Setup(s => s.ChangePasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(DomainError.Conflict("User.PasswordChangeFailed", "fallo")));

        var handler = new ResetPasswordCommandHandler(tokenService.Object, userService.Object);

        var result = await handler.Handle(
            new ResetPasswordCommand("user-1", "token", "123P@$$word!", "123P@$$word!"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("User.PasswordChangeFailed");
    }
}
