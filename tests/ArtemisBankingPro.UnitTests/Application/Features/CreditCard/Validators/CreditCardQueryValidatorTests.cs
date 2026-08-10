using ArtemisBankingPro.Application.Features.CreditCard.Queries;
using ArtemisBankingPro.Application.Features.CreditCard.Validator;

namespace ArtemisBankingPro.UnitTests.Application.Features.CreditCard.Validators;

public sealed class GetCreditCardDetailQueryValidatorTests {
    private readonly GetCreditCardDetailQueryValidator _validator = new();

    [Fact]
    public async Task Validate_ValidQuery_Passes() {
        var result = await _validator.ValidateAsync(new GetCreditCardDetailQuery(1));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ValidPagination_Passes() {
        var result = await _validator.ValidateAsync(
            new GetCreditCardDetailQuery(1, Page: 2, PageSize: 15)
        );

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Validate_InvalidCardId_Fails(int cardId) {
        var result = await _validator.ValidateAsync(new GetCreditCardDetailQuery(cardId));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CardId");
    }

    [Fact]
    public async Task Validate_PageZero_Fails() {
        var result = await _validator.ValidateAsync(new GetCreditCardDetailQuery(1, Page: 0));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Validate_InvalidPageSize_Fails(int pageSize) {
        var result = await _validator.ValidateAsync(
            new GetCreditCardDetailQuery(1, PageSize: pageSize)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PageSize");
    }
}
