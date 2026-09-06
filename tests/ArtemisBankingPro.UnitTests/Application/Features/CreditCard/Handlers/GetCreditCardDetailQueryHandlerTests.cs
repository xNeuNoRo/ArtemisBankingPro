using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.CreditCard.Handlers;
using ArtemisBankingPro.Application.Features.CreditCard.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Enums;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.CreditCard.Handlers;

public sealed class GetCreditCardDetailQueryHandlerTests {
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly FixedToday = new(2026, 8, 7);

    private static CreditCardEntity SeedCard() =>
        CreditCardEntity.Issue(
            "client-1",
            "1234",
            new string('a', 64),
            CvcDigest.Create(new string('c', 64)).Value,
            Money.Create(10_000m).Value,
            "admin-1",
            FixedNow,
            FixedToday).Value;

    private static Mock<IUserRepository> UserRepository() {
        var repository = new Mock<IUserRepository>();
        repository
            .Setup(r => r.GetByIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new UserListDto("client-1", "cliente01", "001", "María", "Gómez", "maria@artemis.com", "Cliente", true, FixedNow)
            );
        return repository;
    }

    private static PageResult<CardConsumptionView> SampleConsumptions() =>
        new(
            [
                new CardConsumptionView(3, new DateTimeOffset(2026, 8, 7, 12, 30, 0, TimeSpan.Zero), 250m, "Comercio Uno", FinancialOperationStatus.Approved),
                new CardConsumptionView(2, new DateTimeOffset(2026, 8, 7, 12, 10, 0, TimeSpan.Zero), 525m, "AVANCE", FinancialOperationStatus.Approved),
                new CardConsumptionView(1, new DateTimeOffset(2026, 8, 7, 11, 0, 0, TimeSpan.Zero), 100m, "Comercio Dos", FinancialOperationStatus.Rejected),
            ],
            TotalCount: 3,
            Page: 1,
            PageSize: 20
        );

    [Fact]
    public async Task Handle_ExistingCard_ReturnsDetailWithConsumptions() {
        var card = SeedCard();
        card.AuthorizeCharge(Money.Create(500m).Value, FixedToday).IsSuccess.Should().BeTrue();

        var cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        cardRepository
            .Setup(r => r.GetConsumptionsPagedAsync(It.IsAny<int>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleConsumptions());

        var handler = new GetCreditCardDetailQueryHandler(
            cardRepository.Object,
            UserRepository().Object
        );

        var result = await handler.Handle(new GetCreditCardDetailQuery(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(card.Id);
        result.Value.MaskedNumber.Should().Be("************1234");
        result.Value.LastFour.Should().Be("1234");
        result.Value.ClientFullName.Should().Be("María Gómez");
        result.Value.CreditLimit.Should().Be(10_000m);
        result.Value.AvailableCredit.Should().Be(9_500m);
        result.Value.CurrentDebt.Should().Be(500m);
        result.Value.Expiration.Should().Be("08/29");
        result.Value.Status.Should().Be("Active");
        result.Value.Consumptions.TotalCount.Should().Be(3);
        result.Value.Consumptions.Items.Should().HaveCount(3);
        result.Value.Consumptions.Items[0].CommerceName.Should().Be("Comercio Uno");
        result.Value.Consumptions.Items[0].Amount.Should().Be(250m);
        result.Value.Consumptions.Items[0].Status.Should().Be("APROBADO");
        result.Value.Consumptions.Items[1].CommerceName.Should().Be("AVANCE");
        result.Value.Consumptions.Items[1].Amount.Should().Be(525m);
        result.Value.Consumptions.Items[1].Status.Should().Be("APROBADO");
        result.Value.Consumptions.Items[2].CommerceName.Should().Be("Comercio Dos");
        result.Value.Consumptions.Items[2].Status.Should().Be("RECHAZADO");
    }

    [Fact]
    public async Task Handle_UnknownCard_ReturnsCardNotFound() {
        var cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreditCardEntity?)null);

        var handler = new GetCreditCardDetailQueryHandler(
            cardRepository.Object,
            new Mock<IUserRepository>().Object
        );

        var result = await handler.Handle(new GetCreditCardDetailQuery(99), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Card.NotFound");
        cardRepository.Verify(
            r => r.GetConsumptionsPagedAsync(It.IsAny<int>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_CardWithoutConsumptions_ReturnsEmptyPage() {
        var cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedCard());
        cardRepository
            .Setup(r => r.GetConsumptionsPagedAsync(It.IsAny<int>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResult<CardConsumptionView>([], TotalCount: 0, Page: 1, PageSize: 20));

        var handler = new GetCreditCardDetailQueryHandler(
            cardRepository.Object,
            UserRepository().Object
        );

        var result = await handler.Handle(new GetCreditCardDetailQuery(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Consumptions.Items.Should().BeEmpty();
        result.Value.Consumptions.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_MissingCustomer_ReturnsEmptyClientName() {
        var cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedCard());
        cardRepository
            .Setup(r => r.GetConsumptionsPagedAsync(It.IsAny<int>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResult<CardConsumptionView>([], TotalCount: 0, Page: 1, PageSize: 20));

        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserListDto?)null);

        var handler = new GetCreditCardDetailQueryHandler(cardRepository.Object, userRepository.Object);

        var result = await handler.Handle(new GetCreditCardDetailQuery(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ClientFullName.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_RepeatedInvocation_ReturnsIdenticalResultWithoutStateChanges() {
        var card = SeedCard();
        var cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        cardRepository
            .Setup(r => r.GetConsumptionsPagedAsync(It.IsAny<int>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleConsumptions());

        var handler = new GetCreditCardDetailQueryHandler(
            cardRepository.Object,
            UserRepository().Object
        );

        var first = await handler.Handle(new GetCreditCardDetailQuery(1), CancellationToken.None);
        var second = await handler.Handle(new GetCreditCardDetailQuery(1), CancellationToken.None);

        Assert.Equivalent(first.Value, second.Value);
        card.CurrentDebt.Should().Be(Money.Zero);
        cardRepository.Verify(
            r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()),
            Times.Exactly(2)
        );
        cardRepository.Verify(
            r => r.GetConsumptionsPagedAsync(It.IsAny<int>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2)
        );
        cardRepository.Verify(r => r.Update(It.IsAny<CreditCardEntity>()), Times.Never);
    }

    [Fact]
    public void Query_RequiresAdministradorRole() {
        var query = new GetCreditCardDetailQuery(1);

        query.RequiredRoles.Should().Equal("Administrador");
        (query is IAuthorize).Should().BeTrue();
    }
}
