using ArtemisBankingPro.Application.Features.SavingsAccounts.ViewModels;
using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.Services;

public interface ISavingsAccountManagementService {
    Task<Result<SavingsAccountListViewModel>> GetAccountsAsync(
        SavingsAccountListViewModel model,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<Result<AccountDetailViewModel>> GetAccountAsync(
        string accountNumber,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<Result<SavingsAccountResponse>> AssignSecondaryAsync(
        string customerUserId,
        AssignSecondaryAccountViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );

    Task<Result> CancelSecondaryAsync(
        string accountNumber,
        CancelSecondaryAccountViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );
}
