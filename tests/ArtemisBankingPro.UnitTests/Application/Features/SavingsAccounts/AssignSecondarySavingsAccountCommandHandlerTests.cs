using ArtemisBankingPro.Application.Features.SavingsAccounts.Commands;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Handlers;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Events;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.SavingsAccounts;

public sealed class AssignSecondarySavingsAccountCommandHandlerTests {
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_ValidClient_CreatesSecondaryAccountAndInitialCredit() {
        var fixture = new Fixture();

        var result = await fixture.Handler.Handle(
            new AssignSecondarySavingsAccountCommand("client-1", 1_500m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.AccountNumber.Should().Be("987654321");
        result.Value.Balance.Should().Be(1_500m);
        result.Value.Type.Should().Be("Secondary");
        result.Value.Status.Should().Be("Active");
        Assert.NotNull(fixture.AddedAccount);
        Assert.IsType<SecondaryAccountOpenedEvent>(fixture.AddedAccount.DomainEvents.Single());
        Assert.NotNull(fixture.AddedOperation);
        fixture.AddedOperation.AccountTransactions.Should().ContainSingle();
        fixture.AddedOperation.AccountTransactions.Single().Amount.Amount.Should().Be(1_500m);
    }

    [Fact]
    public async Task Handle_ZeroInitialAmount_DoesNotCreateFinancialOperation() {
        var fixture = new Fixture();

        var result = await fixture.Handler.Handle(
            new AssignSecondarySavingsAccountCommand("client-1", 0m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Balance.Should().Be(0m);
        Assert.Null(fixture.AddedOperation);
    }

    [Fact]
    public async Task Handle_UnknownCustomer_ReturnsNotFound() {
        var fixture = new Fixture();
        fixture.UserRepository
            .Setup(repository => repository.GetByIdAsync(
                "missing",
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync((UserListDto?)null);

        var result = await fixture.Handler.Handle(
            new AssignSecondarySavingsAccountCommand("missing", 0m),
            CancellationToken.None
        );

        result.Error!.Code.Should().Be("Account.CustomerNotFound");
    }

    [Fact]
    public async Task Handle_InactiveCustomer_ReturnsConflict() {
        var fixture = new Fixture();
        fixture.UserRepository
            .Setup(repository => repository.GetByIdAsync(
                "inactive",
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Client("inactive", isActive: false));

        var result = await fixture.Handler.Handle(
            new AssignSecondarySavingsAccountCommand("inactive", 0m),
            CancellationToken.None
        );

        result.Error!.Code.Should().Be("Account.CustomerNotActive");
    }

    [Fact]
    public async Task Handle_ClientWithoutPrincipalAccount_ReturnsPreconditionFailure() {
        var fixture = new Fixture();
        fixture.SavingsAccountRepository
            .Setup(repository => repository.GetPrincipalByOwnerAsync(
                "client-1",
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync((SavingsAccount?)null);

        var result = await fixture.Handler.Handle(
            new AssignSecondarySavingsAccountCommand("client-1", 0m),
            CancellationToken.None
        );

        result.Error!.Code.Should().Be("Account.NoPrincipalAccount");
    }

    [Fact]
    public async Task Handle_NegativeAmount_ReturnsSpecifiedValidationError() {
        var fixture = new Fixture();

        var result = await fixture.Handler.Handle(
            new AssignSecondarySavingsAccountCommand("client-1", -1m),
            CancellationToken.None
        );

        result.Error!.Code.Should().Be("Account.InitialAmountMustNotBeNegative");
        fixture.NumberGenerator.Verify(
            generator => generator.NextAccountNumberAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    private static UserListDto Client(string id, bool isActive = true) =>
        new(
            id,
            "cliente01",
            "00187654321",
            "María",
            "Gómez",
            "maria@artemis.com",
            "Cliente",
            isActive,
            Now
        );

    private sealed class Fixture {
        public Fixture() {
            UserRepository
                .Setup(repository => repository.GetByIdAsync(
                    "client-1",
                    It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync(Client("client-1"));
            UserRepository
                .Setup(repository => repository.GetRolesAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync(["Cliente"]);

            SavingsAccount principal = SavingsAccount.OpenPrimary(
                "client-1",
                AccountNumber.Create("123456789").Value,
                Money.Zero,
                "admin-1",
                Now
            ).Value;
            SavingsAccountRepository
                .Setup(repository => repository.GetPrincipalByOwnerAsync(
                    "client-1",
                    It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync(principal);
            SavingsAccountRepository
                .Setup(repository => repository.AddAsync(
                    It.IsAny<SavingsAccount>(),
                    It.IsAny<CancellationToken>()
                ))
                .Callback((SavingsAccount account, CancellationToken _) => AddedAccount = account)
                .ReturnsAsync((SavingsAccount account, CancellationToken _) => account);

            FinancialOperationRepository
                .Setup(repository => repository.AddAsync(
                    It.IsAny<FinancialOperation>(),
                    It.IsAny<CancellationToken>()
                ))
                .Callback((FinancialOperation operation, CancellationToken _) =>
                    AddedOperation = operation
                )
                .ReturnsAsync((FinancialOperation operation, CancellationToken _) => operation);

            NumberGenerator
                .Setup(generator => generator.NextAccountNumberAsync(
                    It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync("987654321");

            UnitOfWork
                .Setup(unit => unit.ExecuteInTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Result>>>(),
                    It.IsAny<System.Data.IsolationLevel>(),
                    It.IsAny<CancellationToken>()
                ))
                .Returns(
                    (Func<CancellationToken, Task<Result>> operation,
                        System.Data.IsolationLevel _,
                        CancellationToken ct) => operation(ct)
                );

            Clock.SetupGet(clock => clock.Now).Returns(Now);
            CurrentUser.SetupGet(user => user.UserId).Returns("admin-1");

            Handler = new AssignSecondarySavingsAccountCommandHandler(
                UserRepository.Object,
                SavingsAccountRepository.Object,
                FinancialOperationRepository.Object,
                NumberGenerator.Object,
                UnitOfWork.Object,
                Clock.Object,
                CurrentUser.Object
            );
        }

        public Mock<IUserRepository> UserRepository { get; } = new();
        public Mock<ISavingsAccountRepository> SavingsAccountRepository { get; } = new();
        public Mock<IFinancialOperationRepository> FinancialOperationRepository { get; } = new();
        public Mock<INumberGenerator> NumberGenerator { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IBusinessClock> Clock { get; } = new();
        public Mock<ICurrentUserService> CurrentUser { get; } = new();
        public AssignSecondarySavingsAccountCommandHandler Handler { get; }
        public SavingsAccount? AddedAccount { get; private set; }
        public FinancialOperation? AddedOperation { get; private set; }
    }
}
