using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Cashier.Queries;
using ArtemisBankingPro.Application.Features.Cashier.Validators;

namespace ArtemisBankingPro.UnitTests.Application.Features.Cashier.Validators;

public sealed class GetCashierDashboardQueryValidatorTests {
    private readonly GetCashierDashboardQueryValidator _validator = new();

    [Fact]
    public async Task Validate_ValidQuery_Passes() {
        var result = await _validator.ValidateAsync(
            new GetCashierDashboardQuery("cashier-1", new DateOnly(2026, 8, 6))
        );

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyCashierId_Fails() {
        var result = await _validator.ValidateAsync(
            new GetCashierDashboardQuery("", new DateOnly(2026, 8, 6))
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CashierId");
    }

    [Fact]
    public async Task Validate_DefaultDate_Fails() {
        var result = await _validator.ValidateAsync(
            new GetCashierDashboardQuery("cashier-1", default)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Date");
    }

    [Fact]
    public void Query_RequiresCajeroRole() {
        var query = new GetCashierDashboardQuery("cashier-1", new DateOnly(2026, 8, 6));

        query.RequiredRoles.Should().Equal("Cajero");
        (query is IAuthorize).Should().BeTrue();
    }
}

public sealed class GetCashierOperationsQueryValidatorTests {
    private readonly GetCashierOperationsQueryValidator _validator = new();

    [Fact]
    public async Task Validate_Defaults_Pass() {
        var result = await _validator.ValidateAsync(new GetCashierOperationsQuery());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ValidFiltersAndPagination_Pass() {
        var result = await _validator.ValidateAsync(
            new GetCashierOperationsQuery(
                new DateTimeOffset(2026, 8, 1, 4, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 6, 4, 0, 0, TimeSpan.Zero),
                "LoanPayment",
                Page: 2,
                PageSize: 15
            )
        );

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Deposit")]
    [InlineData("Withdrawal")]
    [InlineData("CardPayment")]
    [InlineData("LoanPayment")]
    [InlineData("ThirdPartyTransfer")]
    [InlineData("thirdpartytransfer")]
    public async Task Validate_ValidOperationTypes_Pass(string operationType) {
        var result = await _validator.ValidateAsync(
            new GetCashierOperationsQuery(OperationType: operationType)
        );

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Transfer")]
    [InlineData("ExpressTransfer")]
    [InlineData("")]
    public async Task Validate_InvalidOperationType_Fails(string operationType) {
        var result = await _validator.ValidateAsync(
            new GetCashierOperationsQuery(OperationType: operationType)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "OperationType");
    }

    [Fact]
    public async Task Validate_PageZero_Fails() {
        var result = await _validator.ValidateAsync(
            new GetCashierOperationsQuery(Page: 0)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Validate_InvalidPageSize_Fails(int pageSize) {
        var result = await _validator.ValidateAsync(
            new GetCashierOperationsQuery(PageSize: pageSize)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PageSize");
    }

    [Fact]
    public async Task Validate_DateFromAfterDateTo_Fails() {
        var result = await _validator.ValidateAsync(
            new GetCashierOperationsQuery(
                new DateTimeOffset(2026, 8, 6, 4, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 1, 4, 0, 0, TimeSpan.Zero)
            )
        );

        result.IsValid.Should().BeFalse();
        result
            .Errors.Should()
            .Contain(e => e.ErrorMessage == "La fecha inicial debe ser anterior o igual a la fecha final.");
    }

    [Fact]
    public void Query_RequiresCajeroAndAdministradorRoles() {
        var query = new GetCashierOperationsQuery();

        query.RequiredRoles.Should().Equal("Cajero", "Administrador");
        (query is IAuthorize).Should().BeTrue();
    }
}
