using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Application.Features.Client.Handlers;
using ArtemisBankingPro.Application.Features.FinancialProcessors;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArtemisBankingPro.UnitTests.Application.Features.Client;

/// <summary>
/// Verifica la transferencia entre cuentas propias (spec §3002-§3166): ambas
/// cuentas deben pertenecer al cliente autenticado y el flujo feliz delega en
/// el procesador de transferencias.
/// </summary>
public sealed class ProcessOwnAccountsTransferCommandHandlerTests {
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.FromHours(-4));

    private sealed class Fixture {
        public Mock<ITransferProcessor> Processor { get; } = new();
        public Mock<ISavingsAccountRepository> Accounts { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IBusinessClock> Clock { get; } = new();
        public Mock<ICurrentUserService> CurrentUser { get; } = new();
        public Mock<IEmailService> Emails { get; } = new();

        public Fixture() {
            CurrentUser.SetupGet(user => user.UserId).Returns("client-1");
            Accounts.Setup(repository => repository.CountActiveByOwnerAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(2);
            Clock.SetupGet(clock => clock.Now).Returns(Now);
            Emails
                .Setup(service => service.SendAsync(
                    It.IsAny<string>(),
                    It.IsAny<IEmailModel>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        public ProcessOwnAccountsTransferCommandHandler Handler =>
            new(
                Processor.Object,
                Accounts.Object,
                Users.Object,
                Clock.Object,
                CurrentUser.Object,
                Emails.Object,
                NullLogger<ProcessOwnAccountsTransferCommandHandler>.Instance
            );

        public void SeedAccount(string number, string owner = "client-1") {
            var account = SavingsAccount
                .OpenPrimary(owner, AccountNumber.Create(number).Value, Money.Create(10_000m).Value, "admin", Now)
                .Value;
            Accounts
                .Setup(repository => repository.GetByNumberAsync(
                    AccountNumber.Create(number).Value,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(account);
        }

        public void SeedOwnAccountsOutcome() {
            Processor
                .Setup(processor => processor.TransferAsync(
                    It.IsAny<SavingsAccount>(),
                    It.IsAny<SavingsAccount>(),
                    It.IsAny<Money>(),
                    It.IsAny<TransferFlow>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    Result.Success(
                        new FinancialOperationOutcome(Guid.NewGuid(), Money.Create(100m).Value, Money.Create(100m).Value, Money.Zero, Now)
                    )
                );
        }
    }

    [Fact]
    public async Task Handle_DestinationOfAnotherClient_ThrowsForbidden() {
        var fixture = new Fixture();
        fixture.SeedAccount("100000001", owner: "client-1");
        fixture.SeedAccount("200000002", owner: "client-2");

        var act = async () => await fixture.Handler.Handle(
            new ProcessOwnAccountsTransferCommand("100000001", "200000002", 100m, "key"),
            CancellationToken.None
        );

        (await act.Should().ThrowAsync<ForbiddenAccessException>())
            .WithMessage("Ambas cuentas deben pertenecer al cliente autenticado.");
    }

    [Fact]
    public async Task Handle_SameAccount_ReturnsError() {
        var fixture = new Fixture();

        var result = await fixture.Handler.Handle(
            new ProcessOwnAccountsTransferCommand("100000001", "100000001", 100m, "key"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidOwnAccountsTransfer_DelegatesToProcessor() {
        var fixture = new Fixture();
        fixture.SeedAccount("100000001", owner: "client-1");
        fixture.SeedAccount("200000002", owner: "client-1");
        fixture.SeedOwnAccountsOutcome();

        var result = await fixture.Handler.Handle(
            new ProcessOwnAccountsTransferCommand("100000001", "200000002", 100m, "key"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        fixture.Processor.Verify(
            processor => processor.TransferAsync(
                It.IsAny<SavingsAccount>(),
                It.IsAny<SavingsAccount>(),
                It.IsAny<Money>(),
                It.IsAny<TransferFlow>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_LessThanTwoActiveAccounts_ReturnsValidationError() {
        var fixture = new Fixture();
        fixture.Accounts
            .Setup(repository => repository.CountActiveByOwnerAsync(
                "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        fixture.SeedAccount("100000001");
        fixture.SeedAccount("200000002");

        var result = await fixture.Handler.Handle(
            new ProcessOwnAccountsTransferCommand("100000001", "200000002", 100m, "key"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.MinimumActiveAccounts");
        fixture.Processor.Verify(
            processor => processor.TransferAsync(
                It.IsAny<SavingsAccount>(),
                It.IsAny<SavingsAccount>(),
                It.IsAny<Money>(),
                It.IsAny<TransferFlow>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_ProcessorFails_PropagatesErrorWithoutEmail() {
        var fixture = new Fixture();
        fixture.SeedAccount("100000001", owner: "client-1");
        fixture.SeedAccount("200000002", owner: "client-1");
        fixture.Processor
            .Setup(processor => processor.TransferAsync(
                It.IsAny<SavingsAccount>(),
                It.IsAny<SavingsAccount>(),
                It.IsAny<Money>(),
                It.IsAny<TransferFlow>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                Result.Failure<FinancialOperationOutcome>(
                    DomainError.Declined("Account.InsufficientFunds", "fondos insuficientes")
                )
            );

        var result = await fixture.Handler.Handle(
            new ProcessOwnAccountsTransferCommand("100000001", "200000002", 100m, "key"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InsufficientFunds");
        fixture.Emails.Verify(
            service => service.SendAsync(
                It.IsAny<string>(),
                It.IsAny<IEmailModel>(),
                It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_UnknownAccount_ReturnsNotFound() {
        var fixture = new Fixture();
        fixture.Accounts
            .Setup(repository => repository.GetByNumberAsync(
                It.IsAny<AccountNumber>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SavingsAccount?)null);

        var result = await fixture.Handler.Handle(
            new ProcessOwnAccountsTransferCommand("100000001", "200000002", 100m, "key"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        fixture.Processor.Verify(
            processor => processor.TransferAsync(
                It.IsAny<SavingsAccount>(),
                It.IsAny<SavingsAccount>(),
                It.IsAny<Money>(),
                It.IsAny<TransferFlow>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never
        );
    }
}
