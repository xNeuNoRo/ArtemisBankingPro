using ArtemisBankingPro.Application.Features.Loans.Commands;
using ArtemisBankingPro.Application.Features.Loans.Queries;
using ArtemisBankingPro.Application.Features.Loans.Validators;

namespace ArtemisBankingPro.UnitTests.Application.Features.Loans.Validators;

public sealed class CreateLoanCommandValidatorTests {
    private readonly CreateLoanCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ValidCommand_Passes() {
        var result = await _validator.ValidateAsync(
            new CreateLoanCommand("client-1", 100000m, 12, 12m)
        );

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyCustomer_Fails() {
        var result = await _validator.ValidateAsync(
            new CreateLoanCommand("", 100000m, 12, 12m)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CustomerUserId");
    }

    [Fact]
    public async Task Validate_ZeroCapital_Fails() {
        var result = await _validator.ValidateAsync(
            new CreateLoanCommand("client-1", 0m, 12, 12m)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CapitalAmount");
    }

    [Fact]
    public async Task Validate_NegativeCapital_Fails() {
        var result = await _validator.ValidateAsync(
            new CreateLoanCommand("client-1", -100m, 12, 12m)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CapitalAmount");
    }

    [Theory]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(24)]
    [InlineData(60)]
    public async Task Validate_ValidTerms_Pass(int term) {
        var result = await _validator.ValidateAsync(
            new CreateLoanCommand("client-1", 100000m, term, 12m)
        );

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(66)]
    public async Task Validate_InvalidTerms_Fail(int term) {
        var result = await _validator.ValidateAsync(
            new CreateLoanCommand("client-1", 100000m, term, 12m)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "TermMonths"
            && e.ErrorMessage == "El plazo seleccionado no es válido."
        );
    }

    [Fact]
    public async Task Validate_NegativeRate_Fails() {
        var result = await _validator.ValidateAsync(
            new CreateLoanCommand("client-1", 100000m, 12, -1m)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "AnnualInterestRate");
    }

    [Fact]
    public async Task Validate_ZeroRate_Passes() {
        var result = await _validator.ValidateAsync(
            new CreateLoanCommand("client-1", 100000m, 12, 0m)
        );

        result.IsValid.Should().BeTrue();
    }
}

public sealed class UpdateLoanRateCommandValidatorTests {
    private readonly UpdateLoanRateCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ValidCommand_Passes() {
        var result = await _validator.ValidateAsync(new UpdateLoanRateCommand(1, 10.5m));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ZeroLoanId_Fails() {
        var result = await _validator.ValidateAsync(new UpdateLoanRateCommand(0, 10.5m));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "LoanId");
    }

    [Fact]
    public async Task Validate_NegativeRate_Fails() {
        var result = await _validator.ValidateAsync(new UpdateLoanRateCommand(1, -1m));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "AnnualInterestRate");
    }
}

public sealed class GetLoansPagedQueryValidatorTests {
    private readonly GetLoansPagedQueryValidator _validator = new();

    [Fact]
    public async Task Validate_Defaults_Pass() {
        var result = await _validator.ValidateAsync(new GetLoansPagedQuery());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_InvalidStatus_Fails() {
        var result = await _validator.ValidateAsync(new GetLoansPagedQuery(Status: "vigentes"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Status");
    }

    [Theory]
    [InlineData("activos")]
    [InlineData("completados")]
    [InlineData("todos")]
    public async Task Validate_ValidStatuses_Pass(string status) {
        var result = await _validator.ValidateAsync(new GetLoansPagedQuery(Status: status));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_PageZero_Fails() {
        var result = await _validator.ValidateAsync(new GetLoansPagedQuery(Page: 0));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }

    [Fact]
    public async Task Validate_PageSizeOverMax_Fails() {
        var result = await _validator.ValidateAsync(new GetLoansPagedQuery(PageSize: 50));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PageSize");
    }
}

public sealed class GetLoanDetailQueryValidatorTests {
    private readonly GetLoanDetailQueryValidator _validator = new();

    [Fact]
    public async Task Validate_ValidCommand_Passes() {
        var result = await _validator.ValidateAsync(new GetLoanDetailQuery(1));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ZeroLoanId_Fails() {
        var result = await _validator.ValidateAsync(new GetLoanDetailQuery(0));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "LoanId");
    }
}
