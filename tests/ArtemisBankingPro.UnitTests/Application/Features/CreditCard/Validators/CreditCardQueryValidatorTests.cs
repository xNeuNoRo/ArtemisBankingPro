using ArtemisBankingPro.Application.Features.CreditCard.Queries;
using ArtemisBankingPro.Application.Features.CreditCard.Validator;
using ArtemisBankingPro.Application.Common.Validation;

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

public sealed class GetCreditCardsPagedQueryValidatorTests {
    private readonly GetCreditCardsPagedQueryValidator _validator = new();

    [Fact]
    public async Task Validate_Defaults_Pass() {
        var result = await _validator.ValidateAsync(new GetCreditCardsPagedQuery());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ValidFilters_Pass() {
        var result = await _validator.ValidateAsync(
            new GetCreditCardsPagedQuery(Page: 2, PageSize: 15, Status: "cancelada", Identification: "001")
        );

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("activa")]
    [InlineData("cancelada")]
    [InlineData("todas")]
    public async Task Validate_ValidStatuses_Pass(string status) {
        var result = await _validator.ValidateAsync(new GetCreditCardsPagedQuery(Status: status));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("vigente")]
    [InlineData("bloqueada")]
    [InlineData("ACTIVA")]
    public async Task Validate_InvalidStatus_Fails(string status) {
        var result = await _validator.ValidateAsync(new GetCreditCardsPagedQuery(Status: status));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Status");
    }

    [Fact]
    public async Task Validate_PageZero_Fails() {
        var result = await _validator.ValidateAsync(new GetCreditCardsPagedQuery(Page: 0));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Validate_InvalidPageSize_Fails(int pageSize) {
        var result = await _validator.ValidateAsync(new GetCreditCardsPagedQuery(PageSize: pageSize));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PageSize");
    }

    [Fact]
    public async Task Validate_IdentificationTooLong_Fails() {
        var result = await _validator.ValidateAsync(
            new GetCreditCardsPagedQuery(
                Identification: new string('1', IdentityValidationLimits.IdentificationMaxLength + 1)
            )
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Identification"
            && e.ErrorMessage == IdentityValidationLimits.IdentificationMaxLengthMessage
        );
    }
}
