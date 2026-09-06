using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Application.Features.CreditCard.Handlers;
using ArtemisBankingPro.Application.Features.CreditCard.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Cards.Enums;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;
using ArtemisBankingPro.Domain.Common.Pagination;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.CreditCard.Handlers;

public sealed class GetCreditCardsPagedQueryHandlerTests {
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static Mock<IUserRepository> UserRepository() {
        var repository = new Mock<IUserRepository>();
        repository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                    new UserListDto("client-1", "cliente01", "001", "María", "Gómez", "maria@artemis.com", "Cliente", true, FixedNow),
                ]
            );
        return repository;
    }

    private static PageResult<CreditCardSummaryDto> SampleCards() =>
        new(
            [
                new CreditCardSummaryDto(2, "************1234", "1234", "client-1", string.Empty, 10_000m, 9_500m, 500m, "08/29", "Active", FixedNow),
                new CreditCardSummaryDto(1, "************5678", "5678", "client-1", string.Empty, 5_000m, 5_000m, 0m, "08/29", "Cancelled", FixedNow),
            ],
            TotalCount: 2,
            Page: 1,
            PageSize: 20
        );

    [Fact]
    public async Task Handle_ReturnsPagedCardsWithCustomerNames() {
        var cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<CreditCardStatus?>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleCards());

        var handler = new GetCreditCardsPagedQueryHandler(cardRepository.Object, UserRepository().Object);

        var result = await handler.Handle(new GetCreditCardsPagedQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items[0].MaskedNumber.Should().Be("************1234");
        result.Value.Items[0].LastFour.Should().Be("1234");
        result.Value.Items[0].ClientId.Should().Be("client-1");
        result.Value.Items[0].ClientFullName.Should().Be("María Gómez");
        result.Value.Items[0].AvailableCredit.Should().Be(9_500m);
        result.Value.Items[0].Expiration.Should().Be("08/29");
        result.Value.Items[0].Status.Should().Be("Active");
        result.Value.Items[1].MaskedNumber.Should().Be("************5678");
        result.Value.Items[1].ClientFullName.Should().Be("María Gómez");
    }

    [Fact]
    public async Task Handle_UnknownIdentification_ReturnsEmptyPage() {
        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(r => r.GetByIdentityDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserListDto?)null);
        var cardRepository = new Mock<ICreditCardRepository>();

        var handler = new GetCreditCardsPagedQueryHandler(cardRepository.Object, userRepository.Object);

        var result = await handler.Handle(
            new GetCreditCardsPagedQuery(Identification: "99999999999"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        cardRepository.Verify(
            r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<CreditCardStatus?>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_MatchingIdentification_PassesResolvedCustomerToRepository() {
        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(r => r.GetByIdentityDocumentAsync("001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new UserListDto("client-9", "cliente09", "001", "Ana", "Pérez", "ana@artemis.com", "Cliente", true, FixedNow)
            );
        var cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<CreditCardStatus?>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResult<CreditCardSummaryDto>([], TotalCount: 0, Page: 1, PageSize: 20));
        userRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var handler = new GetCreditCardsPagedQueryHandler(cardRepository.Object, userRepository.Object);

        var result = await handler.Handle(
            new GetCreditCardsPagedQuery(Identification: "001"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        cardRepository.Verify(
            r => r.GetPagedAsync("client-9", It.IsAny<CreditCardStatus?>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_EmptyResult_ReturnsEmptyPage() {
        var cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<CreditCardStatus?>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResult<CreditCardSummaryDto>([], TotalCount: 0, Page: 1, PageSize: 20));

        var handler = new GetCreditCardsPagedQueryHandler(cardRepository.Object, UserRepository().Object);

        var result = await handler.Handle(new GetCreditCardsPagedQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_MissingCustomers_ReturnsEmptyClientNames() {
        var cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<CreditCardStatus?>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleCards());
        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var handler = new GetCreditCardsPagedQueryHandler(cardRepository.Object, userRepository.Object);

        var result = await handler.Handle(new GetCreditCardsPagedQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().OnlyContain(item => item.ClientFullName == string.Empty);
    }

    [Fact]
    public async Task Handle_RepeatedInvocation_ReturnsIdenticalResultWithoutStateChanges() {
        var cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<CreditCardStatus?>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleCards());

        var handler = new GetCreditCardsPagedQueryHandler(cardRepository.Object, UserRepository().Object);

        var first = await handler.Handle(new GetCreditCardsPagedQuery(), CancellationToken.None);
        var second = await handler.Handle(new GetCreditCardsPagedQuery(), CancellationToken.None);

        Assert.Equivalent(first.Value, second.Value);
        cardRepository.Verify(
            r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<CreditCardStatus?>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2)
        );
        cardRepository.Verify(r => r.Update(It.IsAny<CreditCardEntity>()), Times.Never);
    }

    [Fact]
    public void Query_RequiresAdministradorRole() {
        var query = new GetCreditCardsPagedQuery();

        query.RequiredRoles.Should().Equal("Administrador");
        (query is IAuthorize).Should().BeTrue();
    }
}

public sealed class GetCreditCardsPagedStatusMappingTests {
    private static Mock<IUserRepository> UserRepository() {
        var repository = new Mock<IUserRepository>();
        repository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return repository;
    }

    [Theory]
    [InlineData("activa", CreditCardStatus.Active)]
    [InlineData("cancelada", CreditCardStatus.Cancelled)]
    [InlineData("ACTIVA", CreditCardStatus.Active)]
    [InlineData("todas", null)]
    [InlineData(null, CreditCardStatus.Active)]
    public async Task Handle_StatusFilter_MapsToRepositoryStatus(string? status, CreditCardStatus? expected) {
        var cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<CreditCardStatus?>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResult<CreditCardSummaryDto>([], TotalCount: 0, Page: 1, PageSize: 20));

        var handler = new GetCreditCardsPagedQueryHandler(cardRepository.Object, UserRepository().Object);

        var result = await handler.Handle(new GetCreditCardsPagedQuery(Status: status), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        cardRepository.Verify(
            r => r.GetPagedAsync(
                It.IsAny<string?>(),
                It.Is<CreditCardStatus?>(value => value == expected),
                It.IsAny<PageRequest>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }
}
