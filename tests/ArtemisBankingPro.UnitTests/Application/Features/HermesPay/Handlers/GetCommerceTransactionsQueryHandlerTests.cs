using System.Reflection;
using ArtemisBankingPro.Application.Features.HermesPay.DTOs;
using ArtemisBankingPro.Application.Features.HermesPay.Handlers;
using ArtemisBankingPro.Application.Features.HermesPay.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Merchants.Entities;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.HermesPay.Handlers;

public sealed class GetCommerceTransactionsQueryHandlerTests {
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeCurrentUser : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId => "commerce-user-1";
        public string? UserName => "comercio1";
        public string? Role { get; init; } = "Comercio";
        public int? CommerceId { get; init; }
    }

    private static Merchant NewMerchant(int id, bool active = true) {
        var merchant = Merchant.Create(
            "Tienda Demo",
            null,
            "demo@example.com",
            "8095550101",
            "101000099",
            "admin",
            CreatedAt
        ).Value;
        typeof(Merchant)
            .GetProperty(nameof(Merchant.Id), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(merchant, id);

        if (!active) {
            merchant.Deactivate(CreatedAt.AddHours(1)).IsSuccess.Should().BeTrue();
        }

        return merchant;
    }

    private static PageResult<CommerceTransactionDto> Transactions(params CommerceTransactionDto[] items) =>
        new(items, items.Length, 1, items.Length == 0 ? 1 : 20);

    private static GetCommerceTransactionsQueryHandler CreateHandler(
        out Mock<IMerchantRepository> merchantRepository,
        out Mock<ICreditCardRepository> creditCardRepository,
        ICurrentUserService currentUser,
        Merchant? merchant = null,
        Func<PageRequest, PageResult<CommerceTransactionDto>>? transactions = null
    ) {
        merchantRepository = new Mock<IMerchantRepository>();
        merchantRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(merchant);

        creditCardRepository = new Mock<ICreditCardRepository>();
        creditCardRepository
            .Setup(r => r.GetConsumptionsByMerchantPagedAsync(
                It.IsAny<int>(),
                It.IsAny<PageRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync((int _, PageRequest page, CancellationToken _) =>
                (transactions ?? (request => Transactions()))(page)
            );

        return new GetCommerceTransactionsQueryHandler(
            merchantRepository.Object,
            creditCardRepository.Object,
            currentUser
        );
    }

    [Fact]
    public async Task Handle_CommerceRole_UsesJwtCommerceIdAndIgnoresQueryValue() {
        var merchant = NewMerchant(id: 5);
        var currentUser = new FakeCurrentUser { Role = "Comercio", CommerceId = 5 };
        var handler = CreateHandler(
            out _,
            out var creditCardRepository,
            currentUser,
            merchant,
            _ => Transactions(
                new CommerceTransactionDto("100", CreatedAt, 2500m, "1234", "APROBADO")
            )
        );

        var result = await handler.Handle(
            new GetCommerceTransactionsQuery(CommerceId: 999),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.CommerceId.Should().Be(5);
        result.Value.CommerceName.Should().Be("Tienda Demo");
        result.Value.Data.Should().ContainSingle();
        result.Value.Data[0].CardLastFourDigits.Should().Be("1234");

        creditCardRepository.Verify(r => r.GetConsumptionsByMerchantPagedAsync(
            5,
            It.IsAny<PageRequest>(),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task Handle_CommerceRoleWithoutCommerce_ReturnsForbidden() {
        var handler = CreateHandler(
            out _,
            out _,
            new FakeCurrentUser { Role = "Comercio", CommerceId = null }
        );

        var result = await handler.Handle(
            new GetCommerceTransactionsQuery(CommerceId: 5),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        var error = result.Error!;
        error.Code.Should().Be("Commerce.NotAssociated");
        error.Category.Should().Be(ErrorCategory.Forbidden);
    }

    [Fact]
    public async Task Handle_AdministratorRole_UsesQueryCommerceId() {
        var merchant = NewMerchant(id: 7);
        var handler = CreateHandler(
            out _,
            out var creditCardRepository,
            new FakeCurrentUser { Role = "Administrador", CommerceId = null },
            merchant
        );

        var result = await handler.Handle(
            new GetCommerceTransactionsQuery(CommerceId: 7),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.CommerceId.Should().Be(7);
        creditCardRepository.Verify(r => r.GetConsumptionsByMerchantPagedAsync(
            7,
            It.IsAny<PageRequest>(),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task Handle_AdministratorWithoutCommerceId_ReturnsValidationError() {
        var handler = CreateHandler(
            out _,
            out _,
            new FakeCurrentUser { Role = "Administrador", CommerceId = null }
        );

        var result = await handler.Handle(
            new GetCommerceTransactionsQuery(),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        var error = result.Error!;
        error.Code.Should().Be("Commerce.IdRequired");
        error.Category.Should().Be(ErrorCategory.Validation);
    }

    [Fact]
    public async Task Handle_UnknownCommerce_ReturnsNotFound() {
        var handler = CreateHandler(
            out _,
            out _,
            new FakeCurrentUser { Role = "Administrador", CommerceId = null },
            merchant: null
        );

        var result = await handler.Handle(
            new GetCommerceTransactionsQuery(CommerceId: 404),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.NotFound");
    }

    [Fact]
    public async Task Handle_InactiveCommerce_ReturnsValidationError() {
        var handler = CreateHandler(
            out _,
            out _,
            new FakeCurrentUser { Role = "Administrador", CommerceId = null },
            NewMerchant(id: 5, active: false)
        );

        var result = await handler.Handle(
            new GetCommerceTransactionsQuery(CommerceId: 5),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.Inactive");
    }

    [Fact]
    public async Task Handle_Success_ReturnsPaginationContract() {
        var merchant = NewMerchant(id: 5);
        var handler = CreateHandler(
            out _,
            out _,
            new FakeCurrentUser { Role = "Administrador", CommerceId = null },
            merchant,
            page => new PageResult<CommerceTransactionDto>(
                [new CommerceTransactionDto("1", CreatedAt, 100m, "1234", "APROBADO")],
                3,
                page.Page,
                page.PageSize
            )
        );

        var result = await handler.Handle(
            new GetCommerceTransactionsQuery(CommerceId: 5, Page: 2, PageSize: 5),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Page.Should().Be(2);
        result.Value.PageSize.Should().Be(5);
        result.Value.TotalRecords.Should().Be(3);
        result.Value.TotalPages.Should().Be(1);
        result.Value.Data.Should().ContainSingle();
    }

    [Fact]
    public void Query_RequiresCommerceAndAdministratorRoles() {
        var query = new GetCommerceTransactionsQuery();

        query.RequiredRoles.Should().BeEquivalentTo("Comercio", "Administrador");
    }
}
