using ArtemisBankingPro.Application.Features.SavingsAccounts.Commands;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Handlers;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Validators;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using FluentValidation.TestHelper;
using Mediator;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.SavingsAccounts;

public sealed class CancelSecondarySavingsAccountCommandHandlerTests {
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_WithBalance_TransfersFundsCancelsAndRecordsPairedHistory() {
        var fixture = new Fixture(500m);

        Result<Unit> result = await fixture.Handler.Handle(
            new CancelSecondarySavingsAccountCommand("123456789"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        fixture.Secondary.Status.Should().Be(AccountStatus.Cancelled);
        fixture.Secondary.Balance.Should().Be(Money.Zero);
        fixture.Principal.Balance.Amount.Should().Be(1_500m);
        fixture.Operation!.Kind.Should().Be(FinancialOperationKind.SecondaryAccountClosureTransfer);
        fixture.Operation.AccountTransactions.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithoutBalance_CancelsAndRecordsAccountCancellation() {
        var fixture = new Fixture(0m);

        Result<Unit> result = await fixture.Handler.Handle(
            new CancelSecondarySavingsAccountCommand("123456789"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        fixture.Secondary.Status.Should().Be(AccountStatus.Cancelled);
        fixture.Operation!.Kind.Should().Be(FinancialOperationKind.AccountCancelled);
        fixture.Operation.AccountTransactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PrincipalAccount_ReturnsSpecifiedConflict() {
        var fixture = new Fixture(0m, usePrincipalAsTarget: true);

        Result<Unit> result = await fixture.Handler.Handle(
            new CancelSecondarySavingsAccountCommand("123456789"),
            CancellationToken.None
        );

        result.Error!.Code.Should().Be("Account.PrincipalCannotBeCancelled");
        fixture.Operation.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithoutPrincipal_ReturnsPreconditionFailure() {
        var fixture = new Fixture(500m, hasPrincipal: false);

        Result<Unit> result = await fixture.Handler.Handle(
            new CancelSecondarySavingsAccountCommand("123456789"),
            CancellationToken.None
        );

        result.Error!.Code.Should().Be("Account.NoPrincipalAccount");
        fixture.Secondary.Status.Should().Be(AccountStatus.Active);
    }

    [Fact]
    public void CommandAndValidator_DefineAuthorizationIdempotencyAndNumberValidation() {
        var command = new CancelSecondarySavingsAccountCommand("123456789");
        var invalid = new CancelSecondarySavingsAccountCommandValidator().TestValidate(
            new CancelSecondarySavingsAccountCommand("123")
        );

        command.RequiredRoles.Should().Equal("Administrador");
        command.IdempotencyKey.Should().Be("cancel-secondary-123456789");
        command.RequestFingerprint.Should().Be("123456789");
        invalid.ShouldHaveValidationErrorFor(item => item.AccountNumber);
    }

    private sealed class Fixture {
        public Fixture(decimal balance, bool hasPrincipal = true, bool usePrincipalAsTarget = false) {
            Principal = Open(AccountType.Primary, "987654321", 1_000m);
            Secondary = usePrincipalAsTarget
                ? Open(AccountType.Primary, "123456789", balance)
                : Open(AccountType.Secondary, "123456789", balance);

            SavingsAccountRepository
                .Setup(repository => repository.GetByNumberAsync(
                    AccountNumber.Create("123456789").Value,
                    It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync(Secondary);
            SavingsAccountRepository
                .Setup(repository => repository.GetPrincipalByOwnerAsync(
                    "client-1",
                    It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync(hasPrincipal ? Principal : null);
            FinancialOperationRepository
                .Setup(repository => repository.AddAsync(
                    It.IsAny<FinancialOperation>(),
                    It.IsAny<CancellationToken>()
                ))
                .Callback((FinancialOperation operation, CancellationToken _) => Operation = operation)
                .ReturnsAsync((FinancialOperation operation, CancellationToken _) => operation);
            UnitOfWork
                .Setup(unit => unit.ExecuteInTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Result>>>(),
                    It.IsAny<System.Data.IsolationLevel>(),
                    It.IsAny<CancellationToken>()
                ))
                .Returns((Func<CancellationToken, Task<Result>> operation, System.Data.IsolationLevel _, CancellationToken ct) => operation(ct));
            CurrentUser.SetupGet(user => user.UserId).Returns("admin-1");
            Clock.SetupGet(clock => clock.Now).Returns(Now);

            Handler = new CancelSecondarySavingsAccountCommandHandler(
                SavingsAccountRepository.Object,
                FinancialOperationRepository.Object,
                UnitOfWork.Object,
                CurrentUser.Object,
                Clock.Object
            );
        }

        public Mock<ISavingsAccountRepository> SavingsAccountRepository { get; } = new();
        public Mock<IFinancialOperationRepository> FinancialOperationRepository { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<ICurrentUserService> CurrentUser { get; } = new();
        public Mock<IBusinessClock> Clock { get; } = new();
        public CancelSecondarySavingsAccountCommandHandler Handler { get; }
        public SavingsAccount Principal { get; }
        public SavingsAccount Secondary { get; }
        public FinancialOperation? Operation { get; private set; }

        private static SavingsAccount Open(AccountType type, string number, decimal balance) =>
            type == AccountType.Primary
                ? SavingsAccount.OpenPrimary(
                    "client-1",
                    AccountNumber.Create(number).Value,
                    Money.Create(balance).Value,
                    "admin-1",
                    Now
                ).Value
                : SavingsAccount.OpenSecondary(
                    "client-1",
                    AccountNumber.Create(number).Value,
                    Money.Create(balance).Value,
                    "admin-1",
                    Now
                ).Value;
    }
}
