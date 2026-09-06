using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Application.Features.Merchants.Handlers;
using ArtemisBankingPro.Application.Features.Merchants.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Merchants.Enums;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Merchants.Handlers;

public sealed class GetMerchantsPagedQueryHandlerTests {
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    private sealed record QueryInvocation(MerchantStatus? Status, PageRequest Page);

    private static GetMerchantsPagedQueryHandler CreateHandler(
        List<QueryInvocation> invocations,
        PageResult<MerchantSummaryDto>? result = null
    ) {
        var repository = new Mock<IMerchantRepository>();
        repository
            .Setup(r => r.GetPagedAsync(
                It.IsAny<MerchantStatus?>(),
                It.IsAny<PageRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .Callback<MerchantStatus?, PageRequest, CancellationToken>((status, page, _) =>
                invocations.Add(new QueryInvocation(status, page))
            )
            .ReturnsAsync(result ?? EmptyPage);

        return new GetMerchantsPagedQueryHandler(repository.Object);
    }

    private static PageResult<MerchantSummaryDto> EmptyPage =>
        new([], TotalCount: 0, Page: 1, PageSize: 20);

    private static MerchantSummaryDto MerchantItem(int id) =>
        new(
            id,
            "Tienda Demo",
            "Comercio de prueba",
            "contacto@tiendademo.com",
            "8095551234",
            "101999999",
            IsActive: true,
            HasAssociatedUser: false,
            CreatedAt
        );

    [Fact]
    public async Task Handle_DefaultStatus_RequestsOnlyActiveMerchants() {
        var invocations = new List<QueryInvocation>();
        var handler = CreateHandler(invocations);

        var result = await handler.Handle(
            new GetMerchantsPagedQuery(),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        invocations.Should().ContainSingle();
        invocations[0].Status.Should().Be(MerchantStatus.Active);
        invocations[0].Page.Page.Should().Be(PageRequest.DefaultPage);
        invocations[0].Page.PageSize.Should().Be(PageRequest.DefaultPageSize);
    }

    [Fact]
    public async Task Handle_Todos_RequestsAllStatuses() {
        var invocations = new List<QueryInvocation>();
        var handler = CreateHandler(invocations);

        var result = await handler.Handle(
            new GetMerchantsPagedQuery(Status: "todos"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        invocations[0].Status.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Inactive_RequestsInactiveMerchants() {
        var invocations = new List<QueryInvocation>();
        var handler = CreateHandler(invocations);

        var result = await handler.Handle(
            new GetMerchantsPagedQuery(Status: "inactivo"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        invocations[0].Status.Should().Be(MerchantStatus.Inactive);
    }

    [Fact]
    public async Task Handle_ExplicitStatusActivo_RequestsActiveMerchants() {
        var invocations = new List<QueryInvocation>();
        var handler = CreateHandler(invocations);

        var result = await handler.Handle(
            new GetMerchantsPagedQuery(Status: "activo"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        invocations[0].Status.Should().Be(MerchantStatus.Active);
    }

    [Fact]
    public async Task Handle_PageAndPageSize_AreForwardedToRepository() {
        var invocations = new List<QueryInvocation>();
        var handler = CreateHandler(invocations);

        var result = await handler.Handle(
            new GetMerchantsPagedQuery(Page: 2, PageSize: 5),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        invocations[0].Page.Page.Should().Be(2);
        invocations[0].Page.PageSize.Should().Be(5);
    }

    [Fact]
    public async Task Handle_RepositoryResult_MapsFullContractResponse() {
        var handler = CreateHandler(
            [],
            result: new PageResult<MerchantSummaryDto>(
                [MerchantItem(5)],
                TotalCount: 1,
                Page: 1,
                PageSize: 20
            )
        );

        var result = await handler.Handle(
            new GetMerchantsPagedQuery(),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(20);
        result.Value.TotalRecords.Should().Be(1);
        result.Value.TotalPages.Should().Be(1);
        result.Value.Data.Should().ContainSingle();
        result.Value.Data[0].Id.Should().Be(5);
        result.Value.Data[0].Name.Should().Be("Tienda Demo");
        result.Value.Data[0].Rnc.Should().Be("101999999");
    }

    [Fact]
    public async Task Handle_EmptyRepositoryResult_ReturnsZeroTotals() {
        var handler = CreateHandler([]);

        var result = await handler.Handle(
            new GetMerchantsPagedQuery(),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalRecords.Should().Be(0);
        result.Value.TotalPages.Should().Be(0);
        result.Value.Data.Should().BeEmpty();
    }

    [Fact]
    public void Query_RequiresAdministratorRole() {
        GetMerchantsPagedQuery query = new();

        query.RequiredRoles.Should().BeEquivalentTo("Administrador");
    }
}

