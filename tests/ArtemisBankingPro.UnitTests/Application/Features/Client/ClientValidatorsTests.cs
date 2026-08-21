using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Features.Client.Validators;

namespace ArtemisBankingPro.UnitTests.Application.Features.Client;

/// <summary>
/// Cubre las reglas de validación de la feature Cliente (spec §2325-§2345,
/// §2427-§2446, §2528-§2544, §2607-§2625, §2711-§2732, §2858-§2882,
/// §3044-§3071): requeridos, formato de cuenta de 9 dígitos, montos
/// positivos y cuentas origen/destino distintas.
/// </summary>
public sealed class ClientValidatorsTests {
    private const string ValidAccount = "100000001";

    [Fact]
    public async Task AddBeneficiary_ValidAccount_Passes() {
        var validator = new AddBeneficiaryCommandValidator();

        var result = await validator.ValidateAsync(new AddBeneficiaryCommand(ValidAccount));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("1234567890")]
    [InlineData("ABCDEFGHI")]
    public async Task AddBeneficiary_InvalidAccount_Fails(string account) {
        var validator = new AddBeneficiaryCommandValidator();

        var result = await validator.ValidateAsync(new AddBeneficiaryCommand(account));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DestinationAccountNumber");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RemoveBeneficiary_InvalidId_Fails(int id) {
        var validator = new RemoveBeneficiaryCommandValidator();

        var result = await validator.ValidateAsync(new RemoveBeneficiaryCommand(id));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "BeneficiaryId");
    }

    [Fact]
    public async Task ProcessCashAdvance_Valid_Passes() {
        var validator = new ProcessCashAdvanceCommandValidator();

        var result = await validator.ValidateAsync(
            new ProcessCashAdvanceCommand(1, ValidAccount, 100m, "key")
        );

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ProcessCashAdvance_NonPositiveAmount_Fails(decimal amount) {
        var validator = new ProcessCashAdvanceCommandValidator();

        var result = await validator.ValidateAsync(
            new ProcessCashAdvanceCommand(1, ValidAccount, amount, "key")
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Amount");
    }

    [Fact]
    public async Task ProcessExpressTransaction_SameSourceAndDestination_Fails() {
        var validator = new ProcessExpressTransactionCommandValidator();

        var result = await validator.ValidateAsync(
            new ProcessExpressTransactionCommand(ValidAccount, ValidAccount, 100m, "key")
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "DestinationAccountNumber"
            && e.ErrorMessage == "La cuenta destino no puede ser la misma cuenta de origen."
        );
    }

    [Fact]
    public async Task ProcessExpressTransaction_ValidDistinctAccounts_Passes() {
        var validator = new ProcessExpressTransactionCommandValidator();

        var result = await validator.ValidateAsync(
            new ProcessExpressTransactionCommand(ValidAccount, "100000002", 100m, "key")
        );

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessClientCardPayment_Valid_Passes() {
        var validator = new ProcessClientCardPaymentCommandValidator();

        var result = await validator.ValidateAsync(
            new ProcessClientCardPaymentCommand(1, ValidAccount, 100m, "key")
        );

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessClientCardPayment_InvalidAccount_Fails() {
        var validator = new ProcessClientCardPaymentCommandValidator();

        var result = await validator.ValidateAsync(
            new ProcessClientCardPaymentCommand(1, "12", 100m, "key")
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "AccountNumber");
    }

    [Fact]
    public async Task ProcessClientLoanPayment_Valid_Passes() {
        var validator = new ProcessClientLoanPaymentCommandValidator();

        var result = await validator.ValidateAsync(
            new ProcessClientLoanPaymentCommand(1, ValidAccount, 100m, "key")
        );

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ProcessClientLoanPayment_InvalidLoan_Fails(int loanId) {
        var validator = new ProcessClientLoanPaymentCommandValidator();

        var result = await validator.ValidateAsync(
            new ProcessClientLoanPaymentCommand(loanId, ValidAccount, 100m, "key")
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "LoanId");
    }

    [Fact]
    public async Task ProcessBeneficiaryTransfer_Valid_Passes() {
        var validator = new ProcessBeneficiaryTransferCommandValidator();

        var result = await validator.ValidateAsync(
            new ProcessBeneficiaryTransferCommand(1, ValidAccount, 100m, "key")
        );

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessOwnAccountsTransfer_SameAccount_Fails() {
        var validator = new ProcessOwnAccountsTransferCommandValidator();

        var result = await validator.ValidateAsync(
            new ProcessOwnAccountsTransferCommand(ValidAccount, ValidAccount, 100m, "key")
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "DestinationAccountNumber"
            && e.ErrorMessage == "La cuenta de origen y la cuenta de destino no pueden ser la misma."
        );
    }

    [Fact]
    public async Task ProcessOwnAccountsTransfer_ValidDistinctAccounts_Passes() {
        var validator = new ProcessOwnAccountsTransferCommandValidator();

        var result = await validator.ValidateAsync(
            new ProcessOwnAccountsTransferCommand(ValidAccount, "100000002", 100m, "key")
        );

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task GetCashAdvanceQuote_Valid_Passes() {
        var validator = new GetCashAdvanceQuoteQueryValidator();

        var result = await validator.ValidateAsync(new GetCashAdvanceQuoteQuery(1, 100m));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task GetCashAdvanceQuote_NonPositiveAmount_Fails(decimal amount) {
        var validator = new GetCashAdvanceQuoteQueryValidator();

        var result = await validator.ValidateAsync(new GetCashAdvanceQuoteQuery(1, amount));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Amount");
    }

    [Fact]
    public async Task GetMyAccountTransactions_Valid_Passes() {
        var validator = new GetMyAccountTransactionsQueryValidator();

        var result = await validator.ValidateAsync(
            new GetMyAccountTransactionsQuery(ValidAccount)
        );

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetMyAccountTransactions_InvalidPage_Fails(int page) {
        var validator = new GetMyAccountTransactionsQueryValidator();

        var result = await validator.ValidateAsync(
            new GetMyAccountTransactionsQuery(ValidAccount, Page: page)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }

    [Fact]
    public async Task GetMyAccountTransactions_PageSizeAboveMax_Fails() {
        var validator = new GetMyAccountTransactionsQueryValidator();

        var result = await validator.ValidateAsync(
            new GetMyAccountTransactionsQuery(ValidAccount, PageSize: 50)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PageSize");
    }

    [Theory]
    [InlineData("INVALIDO")]
    [InlineData("CRÉDITO DESCONOCIDO")]
    public async Task GetMyAccountTransactions_InvalidTransactionType_Fails(string transactionType) {
        var validator = new GetMyAccountTransactionsQueryValidator();

        var result = await validator.ValidateAsync(
            new GetMyAccountTransactionsQuery(ValidAccount, TransactionType: transactionType)
        );

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TransactionType");
    }

    [Fact]
    public async Task GetMyCardDetail_Valid_Passes() {
        var validator = new GetMyCardDetailQueryValidator();

        var result = await validator.ValidateAsync(new GetMyCardDetailQuery(1));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task GetMyLoanDetail_Valid_Passes() {
        var validator = new GetMyLoanDetailQueryValidator();

        var result = await validator.ValidateAsync(new GetMyLoanDetailQuery(1));

        result.IsValid.Should().BeTrue();
    }
}
