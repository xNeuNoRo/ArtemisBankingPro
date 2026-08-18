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
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;

namespace ArtemisBankingPro.UnitTests.Application.Features.Client;

/// <summary>
/// Verifica el avance de efectivo del cliente (spec §2812-§3001): la tarjeta
/// y la cuenta destino deben pertenecer al cliente autenticado, y el flujo
/// feliz delega en el procesador de avances.
/// </summary>
public sealed class ProcessCashAdvanceCommandHandlerTests {
    private static readonly DateOnly Today = new(2026, 8, 17);
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.FromHours(-4));

    private sealed class Fixture {
        public Mock<ICashAdvanceProcessor> Processor { get; } = new();
        public Mock<ICreditCardRepository> Cards { get; } = new();
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

        public ProcessCashAdvanceCommandHandler Handler =>
            new(
                Processor.Object,
                Cards.Object,
                Accounts.Object,
                Users.Object,
                Clock.Object,
                CurrentUser.Object,
                Emails.Object,
                NullLogger<ProcessCashAdvanceCommandHandler>.Instance
            );

        public void SeedCard(string owner = "client-1") {
            var card = CreditCardEntity
                .Issue(
                    owner,
                    "1234",
                    "abcd".PadRight(64, '0'),
                    CvcDigest.Create(new string('c', 64)).Value,
                    Money.Create(50_000m).Value,
                    "admin",
                    Now,
                    Today)
                .Value;
            typeof(CreditCardEntity)
                .GetProperty(nameof(CreditCardEntity.Id))!
                .GetSetMethod(true)!
                .Invoke(card, [1]);
            Cards
                .Setup(repository => repository.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(card);
        }

        public void SeedDestination(string owner = "client-1") {
            var account = SavingsAccount
                .OpenPrimary(owner, AccountNumber.Create("100000001").Value, Money.Create(10_000m).Value, "admin", Now)
                .Value;
            Accounts
                .Setup(repository => repository.GetByNumberAsync(
                    AccountNumber.Create("100000001").Value,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(account);
        }

        public void SeedAdvanceOutcome() {
            Processor
                .Setup(processor => processor.AdvanceAsync(
                    It.IsAny<CreditCardEntity>(),
                    It.IsAny<SavingsAccount>(),
                    It.IsAny<Money>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    Result.Success(
                        new FinancialOperationOutcome(Guid.NewGuid(), Money.Create(100m).Value, Money.Create(106.25m).Value, Money.Create(6.25m).Value, Now)
                    )
                );
        }
    }

    [Fact]
    public async Task Handle_OtherClientsCard_ThrowsForbidden() {
        var fixture = new Fixture();
        fixture.SeedCard(owner: "client-2");
        fixture.SeedDestination();

        var act = async () => await fixture.Handler.Handle(
            new ProcessCashAdvanceCommand(1, "100000001", 100m, "key"),
            CancellationToken.None
        );

        (await act.Should().ThrowAsync<ForbiddenAccessException>())
            .WithMessage("La tarjeta no pertenece al cliente autenticado.");
    }

    [Fact]
    public async Task Handle_OtherClientsDestination_ThrowsForbidden() {
        var fixture = new Fixture();
        fixture.SeedCard(owner: "client-1");
        fixture.SeedDestination(owner: "client-2");

        var act = async () => await fixture.Handler.Handle(
            new ProcessCashAdvanceCommand(1, "100000001", 100m, "key"),
            CancellationToken.None
        );

        (await act.Should().ThrowAsync<ForbiddenAccessException>())
            .WithMessage("La cuenta destino no pertenece al cliente autenticado.");
    }

    [Fact]
    public async Task Handle_ValidAdvance_DelegatesToProcessor() {
        var fixture = new Fixture();
        fixture.SeedCard();
        fixture.SeedDestination();
        fixture.SeedAdvanceOutcome();

        var result = await fixture.Handler.Handle(
            new ProcessCashAdvanceCommand(1, "100000001", 100m, "key"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        fixture.Processor.Verify(
            processor => processor.AdvanceAsync(
                It.IsAny<CreditCardEntity>(),
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
        fixture.SeedCard();
        fixture.SeedDestination();
        fixture.Processor
            .Setup(processor => processor.AdvanceAsync(
                It.IsAny<CreditCardEntity>(),
                It.IsAny<SavingsAccount>(),
                It.IsAny<Money>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                Result.Failure<FinancialOperationOutcome>(
                    DomainError.Declined("Card.InsufficientCredit", "crédito insuficiente")
                )
            );

        var result = await fixture.Handler.Handle(
            new ProcessCashAdvanceCommand(1, "100000001", 100m, "key"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Card.InsufficientCredit");
        fixture.Emails.Verify(
            service => service.SendAsync(
                It.IsAny<string>(),
                It.IsAny<IEmailModel>(),
                It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_UnknownCard_ReturnsNotFound() {
        var fixture = new Fixture();
        fixture.Cards
            .Setup(repository => repository.GetByIdAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreditCardEntity?)null);

        var result = await fixture.Handler.Handle(
            new ProcessCashAdvanceCommand(99, "100000001", 100m, "key"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be("La tarjeta indicada no existe.");
    }
}
