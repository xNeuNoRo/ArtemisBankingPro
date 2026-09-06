using ArtemisBankingPro.Application.Features.Cashier.ViewModels;
using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Features.Cashier.Services;

public interface ICashierOperationsService {
    Task<Result<CashierDashboardViewModel>> GetDashboardAsync(CancellationToken ct = default);

    Task<Result<IReadOnlyList<SelectOptionViewModel>>> GetCardOptionsAsync(
        CancellationToken ct = default
    );

    Task<Result<IReadOnlyList<SelectOptionViewModel>>> GetLoanOptionsAsync(
        CancellationToken ct = default
    );

    Task<Result<CashierOperationListViewModel>> GetOperationsAsync(
        CashierOperationListViewModel model,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<Result<TransactionResultViewModel>> DepositAsync(
        DepositViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );

    Task<Result<TransactionResultViewModel>> WithdrawAsync(
        WithdrawalViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );

    Task<Result<TransactionResultViewModel>> PayCardAsync(
        CardPaymentViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );

    Task<Result<TransactionResultViewModel>> PayLoanAsync(
        LoanPaymentViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );

    Task<Result<ThirdPartyTransferResultViewModel>> TransferAsync(
        ThirdPartyTransferViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );

    Task<Result<CashierOperationConfirmationViewModel>> PrepareDepositConfirmationAsync(
        DepositViewModel model,
        CancellationToken ct = default
    );

    Task<Result<CashierOperationConfirmationViewModel>> PrepareWithdrawalConfirmationAsync(
        WithdrawalViewModel model,
        CancellationToken ct = default
    );

    Task<Result<CashierOperationConfirmationViewModel>> PrepareCardPaymentConfirmationAsync(
        CardPaymentViewModel model,
        CancellationToken ct = default
    );

    Task<Result<CashierOperationConfirmationViewModel>> PrepareLoanPaymentConfirmationAsync(
        LoanPaymentViewModel model,
        CancellationToken ct = default
    );

    Task<Result<CashierOperationConfirmationViewModel>> PrepareThirdPartyTransferConfirmationAsync(
        ThirdPartyTransferViewModel model,
        CancellationToken ct = default
    );

    Task<Result<TransactionResultViewModel>> ConfirmDepositAsync(
        DepositViewModel model,
        string confirmationToken,
        CancellationToken ct = default
    );

    Task<Result<TransactionResultViewModel>> ConfirmWithdrawalAsync(
        WithdrawalViewModel model,
        string confirmationToken,
        CancellationToken ct = default
    );

    Task<Result<TransactionResultViewModel>> ConfirmCardPaymentAsync(
        CardPaymentViewModel model,
        string confirmationToken,
        CancellationToken ct = default
    );

    Task<Result<TransactionResultViewModel>> ConfirmLoanPaymentAsync(
        LoanPaymentViewModel model,
        string confirmationToken,
        CancellationToken ct = default
    );

    Task<Result<ThirdPartyTransferResultViewModel>> ConfirmThirdPartyTransferAsync(
        ThirdPartyTransferViewModel model,
        string confirmationToken,
        CancellationToken ct = default
    );
}
