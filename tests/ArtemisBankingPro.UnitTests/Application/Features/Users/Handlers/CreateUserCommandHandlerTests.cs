using Moq;
using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Features.Users.Handlers;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using FinancialOperationEntity = ArtemisBankingPro.Domain.Operations.Entities.FinancialOperation;
using SavingsAccountEntity = ArtemisBankingPro.Domain.Accounts.Entities.SavingsAccount;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArtemisBankingPro.UnitTests.Application.Features.Users.Handlers;

public sealed class CreateUserCommandHandlerTests {
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static CreateUserCommand ValidClientCommand() =>
        new(
            "María",
            "Gómez",
            "00187654321",
            "maria@artemis.com",
            "maria01",
            "123P@$$word!",
            "123P@$$word!",
            "Cliente",
            InitialAmount: 1000m
        );

    private static Mock<IUserAccountService> UserService() {
        var service = new Mock<IUserAccountService>();
        service
            .Setup(s => s.CreateUserAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(
                Result.Success(
                    new CreatedUserInfo(
                        "user-1",
                        "maria01",
                        "maria@artemis.com",
                        "Cliente",
                        false,
                        "María Gómez"
                    )
                )
            );
        return service;
    }

    private static Mock<IUserRepository> UserRepository(bool anyExists = false) {
        var repository = new Mock<IUserRepository>();
        repository
            .Setup(r => r.ExistsByUserNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(anyExists);
        repository
            .Setup(r => r.ExistsByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(anyExists);
        repository
            .Setup(r => r.ExistsByIdentityDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(anyExists);
        return repository;
    }

    private static Mock<IUnitOfWork> UnitOfWork() {
        var uow = new Mock<IUnitOfWork>();
        uow
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result<string>>>>(),
                It.IsAny<System.Data.IsolationLevel>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns(
                (Func<CancellationToken, Task<Result<string>>> operation,
                    System.Data.IsolationLevel _,
                    CancellationToken ct) => operation(ct)
            );
        return uow;
    }

    private static Mock<IAccountTokenService> TokenService() {
        var service = new Mock<IAccountTokenService>();
        service
            .Setup(s => s.GenerateAsync(It.IsAny<string>(), AccountTokenType.Activation, It.IsAny<CancellationToken>()))
            .ReturnsAsync("raw-activation-token");
        return service;
    }

    private static Mock<IBusinessClock> Clock() {
        var clock = new Mock<IBusinessClock>();
        clock.SetupGet(c => c.Now).Returns(FixedNow);
        clock.SetupGet(c => c.NowUtc).Returns(FixedNow);
        return clock;
    }

    private static Mock<ICurrentUserService> CurrentUser() {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.UserId).Returns("admin-1");
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        return user;
    }

    private static CreateUserCommandHandler CreateHandler(
        Mock<IUserRepository>? userRepository = null,
        Mock<IEmailService>? emailService = null,
        Mock<ISavingsAccountRepository>? accountRepository = null,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<IUserAccountService>? userService = null
    ) {
        var numberGenerator = new Mock<INumberGenerator>();
        numberGenerator
            .Setup(n => n.NextAccountNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("123456789");

        var financialRepository = new Mock<IFinancialOperationRepository>();
        financialRepository
            .Setup(r => r.AddAsync(It.IsAny<FinancialOperationEntity>(), It.IsAny<CancellationToken>()))
            .Returns((FinancialOperationEntity op, CancellationToken _) => Task.FromResult(op));

        return new CreateUserCommandHandler(
            (userService ?? UserService()).Object,
            (userRepository ?? UserRepository()).Object,
            (accountRepository ?? new Mock<ISavingsAccountRepository>()).Object,
            financialRepository.Object,
            TokenService().Object,
            (emailService ?? new Mock<IEmailService>()).Object,
            numberGenerator.Object,
            (unitOfWork ?? UnitOfWork()).Object,
            Clock().Object,
            CurrentUser().Object,
            NullLogger<CreateUserCommandHandler>.Instance
        );
    }

    [Fact]
    public async Task Handle_DuplicateUserName_ReturnsConflict() {
        var handler = CreateHandler(userRepository: UserRepository(anyExists: true));

        var result = await handler.Handle(ValidClientCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("User.UserNameExists");
    }

    [Fact]
    public async Task Handle_ClientRole_CreatesPrincipalAccountAndSendsActivationEmail() {
        var emailService = new Mock<IEmailService>();
        var accountRepository = new Mock<ISavingsAccountRepository>();
        var handler = CreateHandler(emailService: emailService, accountRepository: accountRepository);

        var result = await handler.Handle(ValidClientCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.MainAccountNumber.Should().Be("123456789");
        accountRepository.Verify(
            r => r.AddAsync(It.IsAny<SavingsAccountEntity>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
        emailService.Verify(
            s => s.SendAsync(
                "maria@artemis.com",
                It.IsAny<AccountActivationTokenModel>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_ClientRoleWithCallbackUrl_SendsActivationLink() {
        var emailService = new Mock<IEmailService>();
        var handler = CreateHandler(emailService: emailService);

        var result = await handler.Handle(
            ValidClientCommand() with { CallbackUrl = "https://artemis.local" },
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        emailService.Verify(
            s => s.SendAsync(
                "maria@artemis.com",
                It.Is<AccountActivationModel>(m =>
                    m.ActivationLink.StartsWith("https://artemis.local/Auth/Activate?token=")
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_AdminRole_DoesNotCreatePrincipalAccount() {
        var accountRepository = new Mock<ISavingsAccountRepository>();
        var handler = CreateHandler(accountRepository: accountRepository);

        var result = await handler.Handle(
            ValidClientCommand() with { Role = "Administrador", InitialAmount = null },
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.MainAccountNumber.Should().BeNull();
        accountRepository.Verify(
            r => r.AddAsync(It.IsAny<SavingsAccountEntity>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_AccountCreationFails_DeletesUserAndReturnsFailure() {
        var failingUnitOfWork = new Mock<IUnitOfWork>();
        failingUnitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result<string>>>>(),
                It.IsAny<System.Data.IsolationLevel>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(
                Result.Failure<string>(
                    DomainError.Conflict("Concurrency.Conflict", "conflicto al persistir la cuenta")
                )
            );
        var userService = UserService();
        var handler = CreateHandler(
            unitOfWork: failingUnitOfWork,
            userService: userService
        );

        var result = await handler.Handle(ValidClientCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        userService.Verify(
            s => s.DeleteUserAsync("user-1", It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_EmailFails_StillReturnsSuccessAndKeepsUser() {
        var emailService = new Mock<IEmailService>();
        emailService
            .Setup(s => s.SendAsync(
                It.IsAny<string>(),
                It.IsAny<IEmailModel>(),
                It.IsAny<CancellationToken>()
            ))
            .ThrowsAsync(
                new EmailSendException(
                    "Correo de activación",
                    new InvalidOperationException("smtp down")
                )
            );
        var userService = UserService();
        var handler = CreateHandler(emailService: emailService);

        var result = await handler.Handle(ValidClientCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.MainAccountNumber.Should().Be("123456789");
        userService.Verify(
            s => s.DeleteUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }
}
