using ArtemisBankingPro.Application.Features.Merchants.Queries;
using ArtemisBankingPro.Application.Features.Merchants.Validators;

namespace ArtemisBankingPro.UnitTests.Application.Features.Merchants.Validators;

public sealed class GetMerchantsPagedQueryValidatorTests {
    private readonly GetMerchantsPagedQueryValidator _validator = new();

    [Fact]
    public async Task Validate_ValidQuery_Passes() {
        var result = await _validator.ValidateAsync(new GetMerchantsPagedQuery());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_PageZero_Fails() {
        var result = await _validator.ValidateAsync(new GetMerchantsPagedQuery(Page: 0));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }

    [Fact]
    public async Task Validate_PageSizeAboveMaximum_Fails() {
        var result = await _validator.ValidateAsync(new GetMerchantsPagedQuery(PageSize: 21));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PageSize");
    }

    [Fact]
    public async Task Validate_InvalidStatus_UsesSpecMessage() {
        var result = await _validator.ValidateAsync(new GetMerchantsPagedQuery(Status: "cualquiera"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Status"
            && e.ErrorMessage == "El estado solo puede tener los valores activo, inactivo o todos."
        );
    }

    [Theory]
    [InlineData("activo")]
    [InlineData("inactivo")]
    [InlineData("todos")]
    public async Task Validate_AllowedStatuses_Pass(string status) {
        var result = await _validator.ValidateAsync(new GetMerchantsPagedQuery(Status: status));

        result.IsValid.Should().BeTrue();
    }
}

public sealed class GetMerchantByIdQueryValidatorTests {
    private readonly GetMerchantByIdQueryValidator _validator = new();

    [Fact]
    public async Task Validate_ValidId_Passes() {
        var result = await _validator.ValidateAsync(new GetMerchantByIdQuery(5));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ZeroId_Fails() {
        var result = await _validator.ValidateAsync(new GetMerchantByIdQuery(0));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "MerchantId");
    }
}
