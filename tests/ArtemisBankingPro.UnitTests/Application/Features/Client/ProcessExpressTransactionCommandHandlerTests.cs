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
/// Verifica la transacción express (spec §2400-§2505): la cuenta de origen
/// debe pertenecer al cliente, la destino debe existir y ser de terceros, y el
/// flujo feliz delega en el procesador de transferencias.
/// </summary>
public sealed class ProcessExpressTransactionCommandHandlerTests {
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
            Clock.SetupGet(clock => clock.Now).Returns(Now);
            Emails
                .Setup(service => service.SendAsync(
                    It.IsAny<string>(),
                    It.IsAny<IEmailModel>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        public ProcessExpressTransactionCommandHandler Handler =>
            new(
                Processor.Object,
                Accounts.Object,
                Users.Object,
                Clock.Object,
                CurrentUser.Object,
                Emails.Object,
                NullLogger<ProcessExpressTransactionCommandHandler>.Instance
            );

        public void SeedSource(string owner = "client-1", decimal balance = 10_000m) {
            var account = SavingsAccount
                .OpenPrimary(owner, AccountNumber.Create("100000001").Value, Money.Create(balance).Value, "admin", Now)
                .Value;
            Accounts
                .Setup(repository => repository.GetByNumberAsync(
                    AccountNumber.Create("100000001").Value,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(account);
        }

        public void SeedDestination(string owner = "client-2", decimal balance = 500m) {
            var account = SavingsAccount
                .OpenPrimary(owner, AccountNumber.Create("200000002").Value, Money.Create(balance).Value, "admin", Now)
                .Value;
            Accounts
                .Setup(repository => repository.GetByNumberAsync(
                    AccountNumber.Create("200000002").Value,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(account);
        }

        public void SeedThirdPartyOutcome() {
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
    public async Task Handle_OtherClientsSource_ThrowsForbidden() {
        var fixture = new Fixture();
        fixture.SeedSource(owner: "client-9");
        fixture.SeedDestination();

        var act = async () => await fixture.Handler.Handle(
            new ProcessExpressTransactionCommand("100000001", "200000002", 100m, "key"),
            CancellationToken.None
        );

        (await act.Should().ThrowAsync<ForbiddenAccessException>())
            .WithMessage("La cuenta origen no pertenece al cliente autenticado.");
    }

    [Fact]
    public async Task Handle_OwnDestination_ReturnsError() {
        var fixture = new Fixture();
        fixture.SeedSource(owner: "client-1");
        fixture.SeedDestination(owner: "client-1");

        var result = await fixture.Handler.Handle(
            new ProcessExpressTransactionCommand("100000001", "200000002", 100m, "key"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_SameSourceAndDestination_ReturnsError() {
        var fixture = new Fixture();

        var result = await fixture.Handler.Handle(
            new ProcessExpressTransactionCommand("100000001", "100000001", 100m, "key"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidThirdPartyTransfer_DelegatesToProcessor() {
        var fixture = new Fixture();
        fixture.SeedSource();
        fixture.SeedDestination();
        fixture.SeedThirdPartyOutcome();

        var result = await fixture.Handler.Handle(
            new ProcessExpressTransactionCommand("100000001", "200000002", 100m, "key"),
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
    public async Task Handle_ProcessorFails_PropagatesErrorWithoutEmail() {
        var fixture = new Fixture();
        fixture.SeedSource();
        fixture.SeedDestination();
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
            new ProcessExpressTransactionCommand("100000001", "200000002", 100m, "key"),
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
    public async Task Handle_UnknownSourceAccount_ReturnsNotFound() {
        var fixture = new Fixture();
        fixture.Accounts
            .Setup(repository => repository.GetByNumberAsync(
                It.IsAny<AccountNumber>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SavingsAccount?)null);

        var result = await fixture.Handler.Handle(
            new ProcessExpressTransactionCommand("100000001", "200000002", 100m, "key"),
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
