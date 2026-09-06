using ArtemisBankingPro.Application.Features.HermesPay.Queries;
using ArtemisBankingPro.Application.Features.HermesPay.Validators;

namespace ArtemisBankingPro.UnitTests.Application.Features.HermesPay.Validators;

public sealed class GetCommerceTransactionsQueryValidatorTests {
    private readonly GetCommerceTransactionsQueryValidator _validator = new();

    [Fact]
    public async Task Validate_ValidQuery_Passes() {
        var result = await _validator.ValidateAsync(
            new GetCommerceTransactionsQuery(CommerceId: 5, Page: 1, PageSize: 20)
        );

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Validate_PageBelowOne_Fails(int page) {
        var result = await _validator.ValidateAsync(
            new GetCommerceTransactionsQuery(CommerceId: 5, Page: page)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Validate_PageSizeOutOfRange_Fails(int pageSize) {
        var result = await _validator.ValidateAsync(
            new GetCommerceTransactionsQuery(CommerceId: 5, PageSize: pageSize)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PageSize");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Validate_NonPositiveCommerceId_Fails(int commerceId) {
        var result = await _validator.ValidateAsync(
            new GetCommerceTransactionsQuery(CommerceId: commerceId)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CommerceId");
    }
}
