using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Application.Features.Client.Handlers;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Client;

/// <summary>
/// Verifica el alta de beneficiarios (spec §2300-§2352): la cuenta destino
/// debe existir y estar activa, no puede ser propia del cliente autenticado ni
/// estar ya registrada; el alta escribe la relación de forma atómica.
/// </summary>
public sealed class AddBeneficiaryCommandHandlerTests {
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.FromHours(-4));

    private sealed class Fixture {
        public Mock<IBeneficiaryRepository> Beneficiaries { get; } = new();
        public Mock<ISavingsAccountRepository> Accounts { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<ICurrentUserService> CurrentUser { get; } = new();
        public Mock<IBusinessClock> Clock { get; } = new();

        public Fixture(string ownerUserId = "client-1") {
            CurrentUser.SetupGet(user => user.UserId).Returns(ownerUserId);
            Clock.SetupGet(clock => clock.Now).Returns(Now);
            UnitOfWork
                .Setup(unit => unit.ExecuteInTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Result>>>(),
                    It.IsAny<System.Data.IsolationLevel>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (Func<CancellationToken, Task<Result>> operation,
                        System.Data.IsolationLevel _,
                        CancellationToken ct) => operation(ct)
                );
        }

        public AddBeneficiaryCommandHandler Handler =>
            new(
                Beneficiaries.Object,
                Accounts.Object,
                UnitOfWork.Object,
                CurrentUser.Object,
                Clock.Object
            );
    }

    private static SavingsAccount Account(
        string ownerUserId,
        string number,
        AccountStatus status = AccountStatus.Active
    ) {
        SavingsAccount account = status == AccountStatus.Cancelled
            ? SavingsAccount
                .OpenSecondary(ownerUserId, AccountNumber.Create(number).Value, Money.Zero, "admin", Now)
                .Value
            : SavingsAccount
                .OpenPrimary(ownerUserId, AccountNumber.Create(number).Value, Money.Zero, "admin", Now)
                .Value;
        typeof(SavingsAccount)
            .GetProperty(nameof(SavingsAccount.Id))!
            .GetSetMethod(true)!
            .Invoke(account, [42]);
        if (status == AccountStatus.Cancelled) {
            account.Cancel(Now);
        }

        return account;
    }

    [Fact]
    public async Task Handle_ValidAccount_AddsBeneficiary() {
        var fixture = new Fixture();
        var destination = Account("client-2", "200000002");
        fixture.Accounts
            .Setup(repository => repository.GetByNumberAsync(
                AccountNumber.Create("200000002").Value,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(destination);
        fixture.Beneficiaries
            .Setup(repository => repository.ExistsAsync(
                "client-1",
                destination.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await fixture.Handler.Handle(
            new AddBeneficiaryCommand("200000002"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        fixture.Beneficiaries.Verify(
            repository => repository.AddAsync(
                It.Is<Beneficiary>(beneficiary =>
                    beneficiary.OwnerUserId == "client-1"
                    && beneficiary.DestinationAccountId == destination.Id),
                It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_UnknownAccount_ReturnsValidationError() {
        var fixture = new Fixture();
        fixture.Accounts
            .Setup(repository => repository.GetByNumberAsync(
                It.IsAny<AccountNumber>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SavingsAccount?)null);

        var result = await fixture.Handler.Handle(
            new AddBeneficiaryCommand("200000002"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        fixture.Beneficiaries.Verify(
            repository => repository.AddAsync(
                It.IsAny<Beneficiary>(),
                It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_CancelledAccount_ReturnsError() {
        var fixture = new Fixture();
        fixture.Accounts
            .Setup(repository => repository.GetByNumberAsync(
                It.IsAny<AccountNumber>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Account("client-2", "200000002", AccountStatus.Cancelled));

        var result = await fixture.Handler.Handle(
            new AddBeneficiaryCommand("200000002"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        fixture.Beneficiaries.Verify(
            repository => repository.AddAsync(
                It.IsAny<Beneficiary>(),
                It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_OwnAccount_ReturnsSpecMessage() {
        var fixture = new Fixture();
        fixture.Accounts
            .Setup(repository => repository.GetByNumberAsync(
                It.IsAny<AccountNumber>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Account("client-1", "200000002"));

        var result = await fixture.Handler.Handle(
            new AddBeneficiaryCommand("200000002"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be(
            "No puede agregar una cuenta propia como beneficiario. Utilice la opción "
                + "Transferencia para mover fondos entre sus cuentas."
        );
    }

    [Fact]
    public async Task Handle_AlreadyRegistered_ReturnsSpecMessage() {
        var fixture = new Fixture();
        var destination = Account("client-2", "200000002");
        fixture.Accounts
            .Setup(repository => repository.GetByNumberAsync(
                It.IsAny<AccountNumber>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(destination);
        fixture.Beneficiaries
            .Setup(repository => repository.ExistsAsync(
                "client-1",
                destination.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await fixture.Handler.Handle(
            new AddBeneficiaryCommand("200000002"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be(
            "Esta cuenta ya se encuentra registrada como beneficiario."
        );
    }

    [Fact]
    public async Task Handle_InvalidAccountNumber_ReturnsError() {
        var fixture = new Fixture();

        var result = await fixture.Handler.Handle(
            new AddBeneficiaryCommand("123"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        fixture.Accounts.Verify(
            repository => repository.GetByNumberAsync(
                It.IsAny<AccountNumber>(),
                It.IsAny<CancellationToken>()),
            Times.Never
        );
    }
}
