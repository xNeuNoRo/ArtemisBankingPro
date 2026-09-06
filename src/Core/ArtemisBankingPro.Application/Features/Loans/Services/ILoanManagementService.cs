using ArtemisBankingPro.Application.Features.Loans.ViewModels;
using ArtemisBankingPro.Application.Features.Loans.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Features.Loans.Services;

public interface ILoanManagementService {
    Task<Result<LoanListViewModel>> GetLoansAsync(
        LoanListViewModel model,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<Result<LoanDetailViewModel>> GetLoanAsync(
        int loanId,
        CancellationToken ct = default
    );

    Task<Result<CreateLoanResponse>> CreateLoanAsync(
        string customerUserId,
        CreateLoanViewModel model,
        bool confirmHighRisk,
        string idempotencyKey,
        string? confirmationToken = null,
        CancellationToken ct = default
    );

    Task<Result<string>> IssueHighRiskConfirmationAsync(
        string customerUserId,
        CreateLoanViewModel model,
        CancellationToken ct = default
    );

    Task<Result<LoanRateUpdateResponse>> UpdateRateAsync(
        int loanId,
        UpdateLoanRateViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );
}
