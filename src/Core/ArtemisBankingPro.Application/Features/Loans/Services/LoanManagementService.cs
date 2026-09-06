using ArtemisBankingPro.Application.Common.Results;
using ArtemisBankingPro.Application.Features.Loans.Commands;
using ArtemisBankingPro.Application.Features.Loans.DTOs;
using ArtemisBankingPro.Application.Features.Loans.Mapping;
using ArtemisBankingPro.Application.Features.Loans.Queries;
using ArtemisBankingPro.Application.Features.Loans.ViewModels;
using ArtemisBankingPro.Application.Services;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using MapsterMapper;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Loans.Services;

public sealed class LoanManagementService : ILoanManagementService {
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;
    private readonly ConfirmationGuard _confirmationGuard;

    public LoanManagementService(
        IMediator mediator,
        IMapper mapper,
        ConfirmationGuard confirmationGuard
    ) {
        _mediator = mediator;
        _mapper = mapper;
        _confirmationGuard = confirmationGuard;
    }

    public async Task<Result<LoanListViewModel>> GetLoansAsync(
        LoanListViewModel model,
        int page,
        int pageSize,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        Result<PageResult<LoanListDto>> result = await _mediator.Send(
            new GetLoansPagedQuery(page, pageSize, model.Status, model.Identification),
            ct
        );
        return result.MapValue(paged => LoansMappingRegister.ToListViewModel(
            paged.Items,
            paged.Page,
            paged.PageSize,
            paged.TotalCount,
            _mapper,
            model.Status,
            model.Identification
        ));
    }

    public async Task<Result<LoanDetailViewModel>> GetLoanAsync(
        int loanId,
        CancellationToken ct = default
    ) => (await _mediator.Send(new GetLoanDetailQuery(loanId), ct))
        .MapValue(dto => new LoanDetailViewModel {
            LoanId = dto.LoanId,
            LoanNumber = dto.LoanNumber,
            CustomerUserId = dto.CustomerUserId,
            CustomerFullName = dto.CustomerFullName,
            CapitalAmount = dto.CapitalAmount,
            AnnualInterestRate = dto.AnnualInterestRate,
            TermMonths = dto.TermMonths,
            MonthlyInstallment = dto.MonthlyInstallment,
            PendingAmount = dto.PendingAmount,
            Status = dto.Status,
            CustomerPaymentStatus = dto.CustomerPaymentStatus,
            IssuedAt = dto.IssuedAt,
            Amortization = dto.Amortization
                .Select(_mapper.Map<LoanInstallmentViewModel>)
                .ToArray(),
        });

    public async Task<Result<CreateLoanResponse>> CreateLoanAsync(
        string customerUserId,
        CreateLoanViewModel model,
        bool confirmHighRisk,
        string idempotencyKey,
        string? confirmationToken = null,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        CreateLoanCommand command = LoansMappingRegister.ToCreateCommand(
            model,
            _mapper,
            customerUserId,
            confirmHighRisk,
            idempotencyKey
        );

        if (confirmHighRisk || !string.IsNullOrWhiteSpace(confirmationToken)) {
            if (string.IsNullOrWhiteSpace(confirmationToken)) {
                return Result.Failure<CreateLoanResponse>(
                    DomainError.Conflict(
                        "Confirmation.Required",
                        "La confirmación de alto riesgo es requerida."
                    )
                );
            }

            Result confirmation = await _confirmationGuard.ValidateAsync(
                command,
                confirmationToken,
                ct
            );
            if (confirmation.IsFailure) {
                return Result.Failure<CreateLoanResponse>(confirmation.Error!);
            }

            command = command with { IdempotencyKey = confirmationToken };
        }

        return await _mediator.Send(command, ct);
    }

    public async Task<Result<string>> IssueHighRiskConfirmationAsync(
        string customerUserId,
        CreateLoanViewModel model,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        CreateLoanCommand command = LoansMappingRegister.ToCreateCommand(
            model,
            _mapper,
            customerUserId,
            confirmHighRisk: true,
            idempotencyKey: string.Empty
        );
        return await _confirmationGuard.IssueAsync(command, TimeSpan.FromMinutes(30), ct);
    }

    public async Task<Result<LoanRateUpdateResponse>> UpdateRateAsync(
        int loanId,
        UpdateLoanRateViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        UpdateLoanRateCommand command = LoansMappingRegister.ToUpdateRateCommand(
            model,
            _mapper,
            loanId,
            idempotencyKey
        );
        return await _mediator.Send(command, ct);
    }
}
