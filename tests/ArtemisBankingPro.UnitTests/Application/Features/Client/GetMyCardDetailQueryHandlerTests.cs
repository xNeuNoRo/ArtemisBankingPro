using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Client.Handlers;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Enums;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Client;

/// <summary>
/// Verifica el detalle de tarjeta del cliente (spec §2225-§2251): la tarjeta
/// debe pertenecer al cliente autenticado, los consumos se ordenan y mapean a
/// APROBADO/RECHAZADO, y el número completo nunca se expone (solo los últimos
/// cuatro dígitos).
/// </summary>
public sealed class GetMyCardDetailQueryHandlerTests {
    private sealed class Fixture {
        public Mock<ICreditCardRepository> Cards { get; } = new();
        public Mock<ICurrentUserService> CurrentUser { get; } = new();

        public Fixture(string ownerUserId = "client-1") {
            CurrentUser.SetupGet(user => user.UserId).Returns(ownerUserId);
        }

        public GetMyCardDetailQueryHandler Handler => new(Cards.Object, CurrentUser.Object);
    }

    private static CreditCardEntity NewCard(string owner = "client-1") =>
        CreditCardEntity
            .Issue(
                owner,
                "1234",
                "abcd".PadRight(64, '0'),
                CvcDigest.Create(new string('c', 64)).Value,
                Money.Create(50_000m).Value,
                "admin",
                new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.FromHours(-4)),
                new DateOnly(2026, 8, 17))
            .Value;

    private static CardConsumptionView Consumption(
        int id,
        decimal amount,
        string commerce,
        FinancialOperationStatus status
    ) =>
        new(id, new DateTimeOffset(2026, 8, 17, 13, 0, 0, TimeSpan.FromHours(-4)), amount, commerce, status);

    [Fact]
    public async Task Handle_OwnCard_ReturnsDetailWithConsumptions() {
        var fixture = new Fixture();
        var card = NewCard();
        fixture.Cards
            .Setup(repository => repository.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        fixture.Cards
            .Setup(repository => repository.GetConsumptionsPagedAsync(
                card.Id,
                It.IsAny<PageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new PageResult<CardConsumptionView>(
                    [
                        Consumption(1, 2_500m, "Supermercado Demo", FinancialOperationStatus.Approved),
                        Consumption(2, 1_062.50m, "AVANCE", FinancialOperationStatus.Approved),
                        Consumption(3, 800m, "Tienda Demo", FinancialOperationStatus.Rejected),
                    ],
                    3,
                    1,
                    20)
            );

        var result = await fixture.Handler.Handle(
            new GetMyCardDetailQuery(card.Id),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        var detail = result.Value;
        detail.LastFour.Should().Be("1234");
        detail.Consumptions.TotalCount.Should().Be(3);
        detail.Consumptions.Items.Should().Contain(item =>
            item.CommerceName == "Supermercado Demo" && item.Status == "APROBADO"
        );
        detail.Consumptions.Items.Should().Contain(item =>
            item.CommerceName == "Tienda Demo" && item.Status == "RECHAZADO"
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
            new GetMyCardDetailQuery(99),
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
            new GetMyCardDetailQuery(card.Id),
            CancellationToken.None
        );

        (await act.Should().ThrowAsync<ForbiddenAccessException>())
            .WithMessage("La tarjeta no pertenece al cliente autenticado.");
    }
}
