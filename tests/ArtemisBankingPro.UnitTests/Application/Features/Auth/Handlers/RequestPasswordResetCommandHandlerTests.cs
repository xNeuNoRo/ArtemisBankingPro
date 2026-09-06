using Moq;
using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.Handlers;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArtemisBankingPro.UnitTests.Application.Features.Auth.Handlers;

public sealed class RequestPasswordResetCommandHandlerTests {
    private static readonly string[] MvcRoles = ["Administrador", "Cajero", "Cliente"];

    private static readonly PasswordResetUserInfo UserInfo =
        new("user-1", "admin", "admin@artemis.com", "Juan Pérez");

    private static Mock<IUserAccountService> UserService(
        PasswordResetUserInfo? info = null,
        Result? setActiveResult = null
    ) {
        var service = new Mock<IUserAccountService>();
        service
            .Setup(s => s.FindForPasswordResetAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(info);
        service
            .Setup(s => s.SetActiveAsync(It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(setActiveResult ?? Result.Success());
        return service;
    }

    private static Mock<IAccountTokenService> TokenService(
        string rawToken = "raw-token",
        Result<PasswordResetTokenResult>? generationResult = null
    ) {
        var service = new Mock<IAccountTokenService>();
        service
            .Setup(s => s.GeneratePasswordResetAsync(
                "user-1",
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(
                generationResult
                ?? Result.Success(
                    new PasswordResetTokenResult(rawToken, UserInfo.Email, UserInfo.FullName)
                )
            );
        return service;
    }

    private static RequestPasswordResetCommandHandler CreateHandler(
        Mock<IUserAccountService>? userService = null,
        Mock<IAccountTokenService>? tokenService = null,
        Mock<IEmailService>? emailService = null
    ) =>
        new(
            (userService ?? UserService()).Object,
            (tokenService ?? TokenService()).Object,
            (emailService ?? new Mock<IEmailService>()).Object,
            NullLogger<RequestPasswordResetCommandHandler>.Instance
        );

    [Fact]
    public async Task Handle_UnknownUser_ReturnsBadRequest() {
        var handler = CreateHandler(userService: UserService(info: null));

        var result = await handler.Handle(
            new RequestPasswordResetCommand("ghost", MvcRoles),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.ResetUserNotFound");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
    }

    [Fact]
    public async Task Handle_UserWithoutEmail_ReturnsValidationError() {
        var info = UserInfo with { Email = "" };
        var handler = CreateHandler(userService: UserService(info: info));

        var result = await handler.Handle(
            new RequestPasswordResetCommand("admin", MvcRoles),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.ResetEmailMissing");
    }

    [Fact]
    public async Task Handle_ApiFlow_DeactivatesGeneratesTokenAndSendsRawTokenEmail() {
        var userService = UserService(info: UserInfo);
        var tokenService = TokenService("raw-token-123");
        var emailService = new Mock<IEmailService>();

        var handler = CreateHandler(userService, tokenService, emailService);

        var result = await handler.Handle(
            new RequestPasswordResetCommand("admin", MvcRoles),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        userService.Verify(
            s => s.SetActiveAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
        tokenService.Verify(
            s => s.GeneratePasswordResetAsync(
                "user-1",
                It.Is<IReadOnlyCollection<string>>(roles => roles.SequenceEqual(MvcRoles)),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
        emailService.Verify(
            s => s.SendAsync(
                "admin@artemis.com",
                It.Is<PasswordResetTokenModel>(m =>
                    m.Token == "raw-token-123" && m.CustomerName == "Juan Pérez"
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_MvcFlow_SendsResetLinkEmail() {
        var emailService = new Mock<IEmailService>();
        var handler = CreateHandler(
            userService: UserService(info: UserInfo),
            emailService: emailService
        );

        var result = await handler.Handle(
            new RequestPasswordResetCommand("admin", MvcRoles, "https://artemis.local"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        emailService.Verify(
            s => s.SendAsync(
                "admin@artemis.com",
                It.Is<PasswordResetModel>(m =>
                    m.ResetLink.StartsWith("https://artemis.local/Auth/ResetPassword?userId=user-1&token=raw-token")
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_UsesRecipientSnapshotReturnedByProtectedTokenGeneration() {
        var emailService = new Mock<IEmailService>();
        var tokenService = TokenService(
            generationResult: Result.Success(
                new PasswordResetTokenResult(
                    "current-token",
                    "current@artemis.com",
                    "Nombre Actual"
                )
            )
        );
        var handler = CreateHandler(
            userService: UserService(info: UserInfo),
            tokenService: tokenService,
            emailService: emailService
        );

        var result = await handler.Handle(
            new RequestPasswordResetCommand("admin", MvcRoles),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        emailService.Verify(
            service => service.SendAsync(
                "current@artemis.com",
                It.Is<PasswordResetTokenModel>(model =>
                    model.Token == "current-token" && model.CustomerName == "Nombre Actual"
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_TokenGenerationFailure_ReturnsErrorWithoutSendingEmail() {
        var tokenService = TokenService(
            generationResult: Result.Failure<PasswordResetTokenResult>(
                DomainError.Conflict("Auth.ResetCooldown", "cooldown")
            )
        );
        var emailService = new Mock<IEmailService>();
        var handler = CreateHandler(UserService(info: UserInfo), tokenService, emailService);

        var result = await handler.Handle(
            new RequestPasswordResetCommand("admin", MvcRoles),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Auth.ResetCooldown");
        emailService.Verify(
            s => s.SendAsync(
                It.IsAny<string>(),
                It.IsAny<IEmailModel>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_EmailSendFailure_ReturnsConflictButKeepsState() {
        var emailService = new Mock<IEmailService>();
        emailService
            .Setup(s => s.SendAsync(
                It.IsAny<string>(),
                It.IsAny<IEmailModel>(),
                It.IsAny<CancellationToken>()
            ))
            .ThrowsAsync(new EmailSendException("Restablecimiento de contraseña", new InvalidOperationException("smtp down")));

        var handler = CreateHandler(
            userService: UserService(info: UserInfo),
            emailService: emailService
        );

        var result = await handler.Handle(
            new RequestPasswordResetCommand("admin", MvcRoles),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Auth.ResetEmailFailed");
        result.Error.Category.Should().Be(ErrorCategory.Conflict);
    }
}
