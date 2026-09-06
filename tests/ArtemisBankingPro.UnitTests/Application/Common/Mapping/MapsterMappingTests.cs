using ArtemisBankingPro.Application.Common.Mapping;
using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Domain.Merchants.Entities;
using Mapster;
using MapsterMapper;

namespace ArtemisBankingPro.UnitTests.Application.Common.Mapping;

/// <summary>
/// Verifica los perfiles de Mapster (ADR-011): el mapeo Entities → DTOs debe
/// respetar los campos derivados de negocio (estado activo, asociación de
/// usuario) y transportar los campos de contrato.
/// </summary>
public sealed class MapsterMappingTests {
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly ServiceMapper Mapper = new(null!, MapsterConfig.Create());

    private static Merchant NewMerchant(bool active = true, string? associatedUserId = null) {
        var merchant = Merchant.Create(
            "Comercio Uno",
            "Comercio de prueba",
            "comercio@example.com",
            "8095550101",
            "101000099",
            "admin",
            CreatedAt
        ).Value;
        if (!active) {
            merchant.Deactivate(CreatedAt.AddDays(1)).IsSuccess.Should().BeTrue();
        }
        if (associatedUserId is not null) {
            merchant.AssociateUser(associatedUserId, CreatedAt.AddDays(1))
                .IsSuccess
                .Should()
                .BeTrue();
        }
        return merchant;
    }

    [Fact]
    public void MerchantToDetail_Active_ReturnsActiveAndAllContractFields() {
        Merchant merchant = NewMerchant(active: true, associatedUserId: "user-1");

        MerchantDetailDto dto = Mapper.Map<MerchantDetailDto>(merchant);

        dto.Id.Should().Be(merchant.Id);
        dto.Name.Should().Be("Comercio Uno");
        dto.Description.Should().Be("Comercio de prueba");
        dto.Email.Should().Be("comercio@example.com");
        dto.PhoneNumber.Should().Be("8095550101");
        dto.Rnc.Should().Be("101000099");
        dto.IsActive.Should().BeTrue();
        dto.CreatedAt.Should().Be(CreatedAt);
    }

    [Fact]
    public void Configuration_RequiresExplicitMappings() {
        TypeAdapterConfig config = MapsterConfig.Create();

        config.RequireExplicitMapping.Should().BeTrue();
        Action compile = () => config.Compile();

        compile.Should().NotThrow();
    }

    [Fact]
    public void MerchantToDetail_Inactive_MapsActiveFlagFromStatus() {
        Merchant merchant = NewMerchant(active: false);

        MerchantDetailDto dto = Mapper.Map<MerchantDetailDto>(merchant);

        dto.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MerchantToSummary_MapsHasAssociatedUserFromLink(bool hasUser) {
        Merchant merchant = NewMerchant(active: true, associatedUserId: hasUser ? "user-1" : null);

        MerchantSummaryDto dto = Mapper.Map<MerchantSummaryDto>(merchant);

        dto.HasAssociatedUser.Should().Be(hasUser);
        dto.IsActive.Should().BeTrue();
        dto.Name.Should().Be("Comercio Uno");
    }
}
