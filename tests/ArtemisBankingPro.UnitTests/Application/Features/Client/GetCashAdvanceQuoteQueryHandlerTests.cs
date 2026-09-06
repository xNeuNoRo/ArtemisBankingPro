using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Client.Handlers;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Client;

/// <summary>
/// Verifica la cotización de avance de efectivo (spec §2883-§2907): calcula el
/// interés del 6.25%, el total a cargar a la tarjeta y la elegibilidad según
/// el crédito disponible, siempre sobre la tarjeta del cliente autenticado.
/// </summary>
public sealed class GetCashAdvanceQuoteQueryHandlerTests {
    private static readonly DateOnly Today = new(2026, 8, 17);

    private sealed class Fixture {
        public Mock<ICreditCardRepository> Cards { get; } = new();
        public Mock<ICurrentUserService> CurrentUser { get; } = new();
        public Mock<IBusinessClock> Clock { get; } = new();

        public Fixture(string ownerUserId = "client-1") {
            CurrentUser.SetupGet(user => user.UserId).Returns(ownerUserId);
            Clock.SetupGet(clock => clock.Today).Returns(Today);
        }

        public GetCashAdvanceQuoteQueryHandler Handler =>
            new(Cards.Object, CurrentUser.Object, Clock.Object);
    }

    private static CreditCardEntity NewCard(
        string owner = "client-1",
        decimal limit = 50_000m,
        decimal debt = 10_000m
    ) {
        CreditCardEntity card = CreditCardEntity
            .Issue(
                owner,
                "1234",
                "abcd".PadRight(64, '0'),
                CvcDigest.Create(new string('c', 64)).Value,
                Money.Create(limit).Value,
                "admin",
                new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.FromHours(-4)),
                Today)
            .Value;
        if (debt > 0m) {
            card.AuthorizeCharge(Money.Create(debt).Value, Today);
        }

        return card;
    }

    [Fact]
    public async Task Handle_OwnCard_CalculatesInterestAndEligibility() {
        var fixture = new Fixture();
        var card = NewCard(limit: 50_000m, debt: 10_000m);
        fixture.Cards
            .Setup(repository => repository.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        var result = await fixture.Handler.Handle(
            new GetCashAdvanceQuoteQuery(card.Id, Amount: 1_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        var quote = result.Value;
        quote.PrincipalAmount.Should().Be(1_000m);
        quote.InterestAmount.Should().Be(62.50m); // 1,000 x 6.25%.
        quote.TotalToCharge.Should().Be(1_062.50m);
        quote.AvailableCredit.Should().Be(38_937.50m); // 40,000 - 1,062.50.
        quote.IsEligible.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ExceedsAvailableCredit_NotEligible() {
        var fixture = new Fixture();
        var card = NewCard(limit: 500m, debt: 300m);
        fixture.Cards
            .Setup(repository => repository.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        var result = await fixture.Handler.Handle(
            new GetCashAdvanceQuoteQuery(card.Id, Amount: 200m),
            CancellationToken.None
        );

        // Crédito disponible 200; total a cargar 200 + 12.50 = 212.50 > 200.
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalToCharge.Should().Be(212.50m);
        result.Value.IsEligible.Should().BeFalse();
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
            new GetCashAdvanceQuoteQuery(99, 100m),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be("La tarjeta indicada no existe.");
    }

    [Fact]
    public async Task Handle_OtherClientsCard_ThrowsForbidden() {
        var fixture = new Fixture();
        var card = NewCard(owner: "client-2");
        fixture.Cards
            .Setup(repository => repository.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        var act = async () => await fixture.Handler.Handle(
            new GetCashAdvanceQuoteQuery(card.Id, 100m),
            CancellationToken.None
        );

        (await act.Should().ThrowAsync<ForbiddenAccessException>())
            .WithMessage("La tarjeta no pertenece al cliente autenticado.");
    }
}
