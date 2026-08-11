using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Application.Features.Cashier.Queries;
using ArtemisBankingPro.Application.Features.Cashier.Validators;
using ArtemisBankingPro.Domain.Operations.Enums;

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

public sealed class GetCashierOperationsPagedQueryValidatorTests {
    private readonly GetCashierOperationsPagedQueryValidator _validator = new();

    [Fact]
    public async Task Validate_Defaults_Pass() {
        var result = await _validator.ValidateAsync(new GetCashierOperationsPagedQuery("cashier-1"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ValidFiltersAndPagination_Pass() {
        var result = await _validator.ValidateAsync(
            new GetCashierOperationsPagedQuery(
                "cashier-1",
                new CashierOperationFilters(
                    FinancialOperationKind.Deposit,
                    FinancialOperationStatus.Approved,
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 6)
                ),
                Page: 2,
                PageSize: 15
            )
        );

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyCashierId_Fails() {
        var result = await _validator.ValidateAsync(new GetCashierOperationsPagedQuery(""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CashierId");
    }

    [Fact]
    public async Task Validate_PageZero_Fails() {
        var result = await _validator.ValidateAsync(
            new GetCashierOperationsPagedQuery("cashier-1", Page: 0)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Validate_InvalidPageSize_Fails(int pageSize) {
        var result = await _validator.ValidateAsync(
            new GetCashierOperationsPagedQuery("cashier-1", PageSize: pageSize)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PageSize");
    }

    [Fact]
    public async Task Validate_InvalidKind_Fails() {
        var result = await _validator.ValidateAsync(
            new GetCashierOperationsPagedQuery(
                "cashier-1",
                new CashierOperationFilters(Kind: (FinancialOperationKind)999)
            )
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Filters.Kind.Value");
    }

    [Fact]
    public async Task Validate_InvalidStatus_Fails() {
        var result = await _validator.ValidateAsync(
            new GetCashierOperationsPagedQuery(
                "cashier-1",
                new CashierOperationFilters(Status: (FinancialOperationStatus)999)
            )
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Filters.Status.Value");
    }

    [Fact]
    public async Task Validate_FromDateAfterToDate_Fails() {
        var result = await _validator.ValidateAsync(
            new GetCashierOperationsPagedQuery(
                "cashier-1",
                new CashierOperationFilters(
                    FromDate: new DateOnly(2026, 8, 6),
                    ToDate: new DateOnly(2026, 8, 1)
                )
            )
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Filters");
    }

    [Fact]
    public void Query_RequiresCajeroRole() {
        var query = new GetCashierOperationsPagedQuery("cashier-1");

        query.RequiredRoles.Should().Equal("Cajero");
        (query is IAuthorize).Should().BeTrue();
    }
}
