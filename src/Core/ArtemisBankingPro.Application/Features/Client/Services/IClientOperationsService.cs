using ArtemisBankingPro.Application.Features.Client.ViewModels;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Features.Client.Services;

public interface IClientOperationsService {
    Task<Result<ClientDashboardViewModel>> GetDashboardAsync(CancellationToken ct = default);

    Task<Result<MyAccountTransactionsViewModel>> GetAccountTransactionsAsync(
        MyAccountTransactionsViewModel model,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<Result<MyLoanDetailViewModel>> GetLoanAsync(int loanId, CancellationToken ct = default);

    Task<Result<MyCardDetailViewModel>> GetCardAsync(
        int cardId,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<Result<BeneficiaryListViewModel>> GetBeneficiariesAsync(
        CancellationToken ct = default
    );

    Task<Result<CashAdvanceQuoteViewModel>> GetCashAdvanceQuoteAsync(
        CashAdvanceQuoteViewModel model,
        CancellationToken ct = default
    );

    Task<Result<ClientTransferTargetViewModel>> GetExpressTransferTargetAsync(
        string destinationAccountNumber,
        CancellationToken ct = default
    );

    Task<Result<string>> IssueAddBeneficiaryConfirmationAsync(
        AddBeneficiaryViewModel model,
        CancellationToken ct = default
    );

    Task<Result<string>> IssueRemoveBeneficiaryConfirmationAsync(
        int beneficiaryId,
        CancellationToken ct = default
    );

    Task<Result<string>> IssueExpressTransferConfirmationAsync(
        ExpressTransactionViewModel model,
        CancellationToken ct = default
    );

    Task<Result<string>> IssueBeneficiaryTransferConfirmationAsync(
        BeneficiaryTransferViewModel model,
        CancellationToken ct = default
    );

    Task<Result<string>> IssueOwnAccountsTransferConfirmationAsync(
        OwnAccountsTransferViewModel model,
        CancellationToken ct = default
    );

    Task<Result<string>> IssueCardPaymentConfirmationAsync(
        ClientCardPaymentViewModel model,
        CancellationToken ct = default
    );

    Task<Result<string>> IssueLoanPaymentConfirmationAsync(
        ClientLoanPaymentViewModel model,
        CancellationToken ct = default
    );

    Task<Result<string>> IssueCashAdvanceConfirmationAsync(
        CashAdvanceViewModel model,
        CancellationToken ct = default
    );

    Task<Result> AddBeneficiaryAsync(
        AddBeneficiaryViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );

    Task<Result> RemoveBeneficiaryAsync(
        int beneficiaryId,
        RemoveBeneficiaryViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );

    Task<Result> ExpressTransferAsync(
        ExpressTransactionViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );

    Task<Result> BeneficiaryTransferAsync(
        BeneficiaryTransferViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );

    Task<Result> OwnAccountsTransferAsync(
        OwnAccountsTransferViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );

    Task<Result> PayCardAsync(
        ClientCardPaymentViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );

    Task<Result> PayLoanAsync(
        ClientLoanPaymentViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );

    Task<Result> CashAdvanceAsync(
        CashAdvanceViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );
}
