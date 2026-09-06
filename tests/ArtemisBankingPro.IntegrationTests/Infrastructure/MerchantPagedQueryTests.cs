using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Merchants.Enums;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

/// <summary>
/// Prueba del listado paginado de comercios sobre SQL Server real
/// (spec §40, GET /api/commerce).
/// </summary>
[Collection("SqlServer")]
public sealed class MerchantPagedQueryTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private static Merchant NewMerchant(
        string name,
        string email,
        string rnc,
        bool active,
        DateTimeOffset createdAt
    ) {
        var merchant = Merchant.Create(
            name,
            null,
            email,
            "8095550101",
            rnc,
            "admin",
            createdAt).Value;
        if (!active) {
            merchant.Deactivate(createdAt.AddHours(1)).IsSuccess.Should().BeTrue();
        }

        return merchant;
    }

    private async Task SeedMerchantsAsync() {
        await WithContextAsync(async context => {
            context.Merchants.AddRange(
                NewMerchant(
                    "Comercio Nuevo",
                    "nuevo@example.com",
                    "101000001",
                    active: true,
                    new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.FromHours(-4))),
                NewMerchant(
                    "Comercio Antiguo",
                    "antiguo@example.com",
                    "101000002",
                    active: true,
                    new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.FromHours(-4))),
                NewMerchant(
                    "Comercio Inactivo",
                    "inactivo@example.com",
                    "101000003",
                    active: false,
                    new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(-4)))
            );
            await context.SaveChangesAsync();
        });
    }

    private Task<PageResult<MerchantSummaryDto>> QueryAsync(
        MerchantStatus? status,
        PageRequest page
    ) =>
        WithContextAsync(async context => {
            var repository = new MerchantRepository(context);
            return await repository.GetPagedAsync(status, page);
        });

    [Fact]
    public async Task GetPagedAsync_ActiveFilter_ReturnsOnlyActiveNewestFirst() {
        await SeedMerchantsAsync();

        PageResult<MerchantSummaryDto> result = await QueryAsync(
            MerchantStatus.Active,
            new PageRequest(1, 20)
        );

        result.TotalCount.Should().Be(2);
        result.TotalPages.Should().Be(1);
        result.Items.Should().HaveCount(2);
        result.Items[0].Name.Should().Be("Comercio Nuevo");
        result.Items[1].Name.Should().Be("Comercio Antiguo");
        result.Items.Should().OnlyContain(merchant => merchant.IsActive);
        result.Items.Should().OnlyContain(merchant => !merchant.HasAssociatedUser);
    }

    [Fact]
    public async Task GetPagedAsync_NullStatus_ReturnsAllStatuses() {
        await SeedMerchantsAsync();

        PageResult<MerchantSummaryDto> result = await QueryAsync(
            null,
            new PageRequest(1, 20)
        );

        result.TotalCount.Should().Be(3);
        result.Items.Should().ContainSingle(merchant => !merchant.IsActive);
        result.Items[0].Name.Should().Be("Comercio Nuevo");
        result.Items[2].Name.Should().Be("Comercio Antiguo");
    }

    [Fact]
    public async Task GetPagedAsync_InactiveFilter_ReturnsOnlyInactive() {
        await SeedMerchantsAsync();

        PageResult<MerchantSummaryDto> result = await QueryAsync(
            MerchantStatus.Inactive,
            new PageRequest(1, 20)
        );

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle();
        result.Items[0].Name.Should().Be("Comercio Inactivo");
        result.Items[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetPagedAsync_SecondPage_PreservesTotalsAndOrdering() {
        await SeedMerchantsAsync();

        PageResult<MerchantSummaryDto> first = await QueryAsync(
            null,
            new PageRequest(1, 2)
        );
        PageResult<MerchantSummaryDto> second = await QueryAsync(
            null,
            new PageRequest(2, 2)
        );

        first.Items.Should().HaveCount(2);
        first.TotalCount.Should().Be(3);
        second.Items.Should().HaveCount(1);
        second.TotalCount.Should().Be(3);
        second.TotalPages.Should().Be(2);
        second.Items[0].Name.Should().Be("Comercio Antiguo");
    }

    [Fact]
    public async Task GetPagedAsync_HasAssociatedUser_ReflectsAssociation() {
        await WithContextAsync(async context => {
            var merchant = NewMerchant(
                "Comercio Con Usuario",
                "conusuario@example.com",
                "101000004",
                active: true,
                new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.FromHours(-4)));
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();

            merchant.AssociateUser("user-1", merchant.CreatedAt.AddDays(1))
                .IsSuccess.Should().BeTrue();
            await context.SaveChangesAsync();
        });

        PageResult<MerchantSummaryDto> result = await QueryAsync(
            MerchantStatus.Active,
            new PageRequest(1, 20)
        );

        result.Items.Should().ContainSingle();
        result.Items[0].HasAssociatedUser.Should().BeTrue();
    }
}
