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
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArtemisBankingPro.UnitTests.Application.Features.Client;

/// <summary>
/// Verifica el pago del cliente a su préstamo (spec §2587-§2688): el préstamo
/// y la cuenta deben pertenecer al cliente autenticado, y el flujo feliz
/// delega en el procesador de pagos a préstamo.
/// </summary>
public sealed class ProcessClientLoanPaymentCommandHandlerTests {
    private static readonly DateOnly Today = new(2026, 8, 17);
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.FromHours(-4));

    private sealed class Fixture {
        public Mock<ILoanPaymentProcessor> Processor { get; } = new();
        public Mock<ILoanRepository> Loans { get; } = new();
        public Mock<ISavingsAccountRepository> Accounts { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IBusinessClock> Clock { get; } = new();
        public Mock<ICurrentUserService> CurrentUser { get; } = new();
        public Mock<IEmailService> Emails { get; } = new();

        public Fixture() {
            CurrentUser.SetupGet(user => user.UserId).Returns("client-1");
            Clock.SetupGet(clock => clock.Now).Returns(Now);
            Clock.SetupGet(clock => clock.Today).Returns(Today);
            Emails
                .Setup(service => service.SendAsync(
                    It.IsAny<string>(),
                    It.IsAny<IEmailModel>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        public ProcessClientLoanPaymentCommandHandler Handler =>
            new(
                Processor.Object,
                Loans.Object,
                Accounts.Object,
                Users.Object,
                Clock.Object,
                CurrentUser.Object,
                Emails.Object,
                NullLogger<ProcessClientLoanPaymentCommandHandler>.Instance
            );

        public void SeedLoan(string owner = "client-1") {
            Loan loan = Loan
                .Issue(
                    owner,
                    LoanNumber.Create("300000001").Value,
                    Money.Create(10_000m).Value,
                    12,
                    InterestRate.Create(12m).Value,
                    "admin",
                    Now,
                    Today)
                .Value;
            Loans
                .Setup(repository => repository.GetWithInstallmentsByIdAsync(
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(loan);
        }

        public void SeedAccount(string owner = "client-1") {
            var account = SavingsAccount
                .OpenPrimary(owner, AccountNumber.Create("100000001").Value, Money.Create(10_000m).Value, "admin", Now)
                .Value;
            Accounts
                .Setup(repository => repository.GetByNumberAsync(
                    AccountNumber.Create("100000001").Value,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(account);
        }

        public void SeedPaymentOutcome() {
            Processor
                .Setup(processor => processor.PayAsync(
                    It.IsAny<Loan>(),
                    It.IsAny<SavingsAccount>(),
                    It.IsAny<Money>(),
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
    public async Task Handle_OtherClientsLoan_ThrowsForbidden() {
        var fixture = new Fixture();
        fixture.SeedLoan(owner: "client-2");
        fixture.SeedAccount();

        var act = async () => await fixture.Handler.Handle(
            new ProcessClientLoanPaymentCommand(1, "100000001", 100m, "key"),
            CancellationToken.None
        );

        (await act.Should().ThrowAsync<ForbiddenAccessException>())
            .WithMessage("El préstamo no pertenece al cliente autenticado.");
    }

    [Fact]
    public async Task Handle_OtherClientsAccount_ThrowsForbidden() {
        var fixture = new Fixture();
        fixture.SeedLoan(owner: "client-1");
        fixture.SeedAccount(owner: "client-2");

        var act = async () => await fixture.Handler.Handle(
            new ProcessClientLoanPaymentCommand(1, "100000001", 100m, "key"),
            CancellationToken.None
        );

        (await act.Should().ThrowAsync<ForbiddenAccessException>())
            .WithMessage("La cuenta no pertenece al cliente autenticado.");
    }

    [Fact]
    public async Task Handle_ValidPayment_DelegatesToProcessor() {
        var fixture = new Fixture();
        fixture.SeedLoan();
        fixture.SeedAccount();
        fixture.SeedPaymentOutcome();

        var result = await fixture.Handler.Handle(
            new ProcessClientLoanPaymentCommand(1, "100000001", 100m, "key"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        fixture.Processor.Verify(
            processor => processor.PayAsync(
                It.IsAny<Loan>(),
                It.IsAny<SavingsAccount>(),
                It.IsAny<Money>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_ProcessorFails_PropagatesErrorWithoutEmail() {
        var fixture = new Fixture();
        fixture.SeedLoan();
        fixture.SeedAccount();
        fixture.Processor
            .Setup(processor => processor.PayAsync(
                It.IsAny<Loan>(),
                It.IsAny<SavingsAccount>(),
                It.IsAny<Money>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                Result.Failure<FinancialOperationOutcome>(
                    DomainError.Declined("Account.InsufficientFunds", "fondos insuficientes")
                )
            );

        var result = await fixture.Handler.Handle(
            new ProcessClientLoanPaymentCommand(1, "100000001", 100m, "key"),
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
    public async Task Handle_UnknownLoan_ReturnsNotFound() {
        var fixture = new Fixture();
        fixture.Loans
            .Setup(repository => repository.GetWithInstallmentsByIdAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Loan?)null);

        var result = await fixture.Handler.Handle(
            new ProcessClientLoanPaymentCommand(99, "100000001", 100m, "key"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be("El préstamo indicado no existe.");
    }
}
