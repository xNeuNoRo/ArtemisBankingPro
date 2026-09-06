using Moq;
using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.Handlers;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.UnitTests.Application.Features.Auth.Handlers;

public sealed class ActivateAccountCommandHandlerTests {
    private static Mock<IAccountTokenService> TokenService(AccountTokenVerificationResult result) {
        var service = new Mock<IAccountTokenService>();
        service
            .Setup(s => s.CompleteActivationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(result));
        return service;
    }

    [Fact]
    public async Task Handle_ValidToken_ActivatesUser() {
        var tokenService = TokenService(AccountTokenVerificationResult.Valid);

        var handler = new ActivateAccountCommandHandler(tokenService.Object);

        var result = await handler.Handle(
            new ActivateAccountCommand("token-123"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        tokenService.Verify(
            s => s.CompleteActivationAsync("token-123", It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_InvalidToken_ReturnsValidationErrorAndDoesNotActivate() {
        var tokenService = TokenService(AccountTokenVerificationResult.Invalid);

        var handler = new ActivateAccountCommandHandler(tokenService.Object);

        var result = await handler.Handle(
            new ActivateAccountCommand("bad-token"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InvalidActivationToken");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
    }

    [Fact]
    public async Task Handle_ExpiredToken_ReturnsExpirationMessage() {
        var handler = new ActivateAccountCommandHandler(
            TokenService(AccountTokenVerificationResult.Expired).Object
        );

        var result = await handler.Handle(
            new ActivateAccountCommand("expired"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InvalidActivationToken");
    }

    [Fact]
    public async Task Handle_AlreadyUsedToken_ReturnsUsedMessage() {
        var handler = new ActivateAccountCommandHandler(
            TokenService(AccountTokenVerificationResult.AlreadyUsed).Object
        );

        var result = await handler.Handle(
            new ActivateAccountCommand("used"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InvalidActivationToken");
    }
}

public sealed class ResetPasswordCommandHandlerTests {
    private static Mock<IAccountTokenService> TokenService(AccountTokenVerificationResult result) {
        var service = new Mock<IAccountTokenService>();
        service
            .Setup(s => s.CompletePasswordResetAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Result.Success(result));
        return service;
    }

    [Fact]
    public async Task Handle_ValidToken_ChangesPasswordAndReactivates() {
        var tokenService = TokenService(AccountTokenVerificationResult.Valid);

        var handler = new ResetPasswordCommandHandler(tokenService.Object);

        var result = await handler.Handle(
            new ResetPasswordCommand("user-1", "token", "123P@$$word!", "123P@$$word!"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        tokenService.Verify(s => s.CompletePasswordResetAsync(
            "user-1", "token", "123P@$$word!", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExpiredToken_ReturnsExpirationMessage() {
        var handler = new ResetPasswordCommandHandler(
            TokenService(AccountTokenVerificationResult.Expired).Object
        );

        var result = await handler.Handle(
            new ResetPasswordCommand("user-1", "expired", "123P@$$word!", "123P@$$word!"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InvalidResetToken");
    }

    [Fact]
    public async Task Handle_AlreadyUsedToken_ReturnsUsedMessage() {
        var handler = new ResetPasswordCommandHandler(
            TokenService(AccountTokenVerificationResult.AlreadyUsed).Object
        );

        var result = await handler.Handle(
            new ResetPasswordCommand("user-1", "used", "123P@$$word!", "123P@$$word!"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InvalidResetToken");
    }

    [Fact]
    public async Task Handle_InvalidToken_ReturnsInvalidMessage() {
        var handler = new ResetPasswordCommandHandler(
            TokenService(AccountTokenVerificationResult.Invalid).Object
        );

        var result = await handler.Handle(
            new ResetPasswordCommand("user-1", "invalid", "123P@$$word!", "123P@$$word!"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InvalidResetToken");
    }

    [Fact]
    public async Task Handle_PasswordChangeFailure_PropagatesError() {
        var tokenService = new Mock<IAccountTokenService>();
        tokenService
            .Setup(s => s.CompletePasswordResetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AccountTokenVerificationResult>(
                DomainError.Conflict("User.PasswordChangeFailed", "fallo")));

        var handler = new ResetPasswordCommandHandler(tokenService.Object);

        var result = await handler.Handle(
            new ResetPasswordCommand("user-1", "token", "123P@$$word!", "123P@$$word!"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("User.PasswordChangeFailed");
    }
}
