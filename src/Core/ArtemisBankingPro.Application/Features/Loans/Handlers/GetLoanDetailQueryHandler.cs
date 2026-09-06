using ArtemisBankingPro.Application.Features.Loans.DTOs;
using ArtemisBankingPro.Application.Features.Loans.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Loans.Handlers;

/// <summary>
/// Detalle de un préstamo con su tabla de amortización completa.
/// </summary>
public sealed class GetLoanDetailQueryHandler
    : IRequestHandler<GetLoanDetailQuery, Result<LoanDetailDto>> {
    private readonly ILoanRepository _loanRepository;
    private readonly IUserRepository _userRepository;

    public GetLoanDetailQueryHandler(
        ILoanRepository loanRepository,
        IUserRepository userRepository
    ) {
        _loanRepository = loanRepository;
        _userRepository = userRepository;
    }

    public async ValueTask<Result<LoanDetailDto>> Handle(
        GetLoanDetailQuery message,
        CancellationToken cancellationToken
    ) {
        var loan = await _loanRepository.GetWithInstallmentsByIdAsync(
            message.LoanId,
            cancellationToken
        );
        if (loan is null) {
            return Result.Failure<LoanDetailDto>(
                DomainError.NotFound(
                    "Loan.NotFound",
                    "El préstamo indicado no existe."
                )
            );
        }

        var customer = await _userRepository.GetByIdAsync(
            loan.CustomerUserId,
            cancellationToken
        );

        var amortization = loan.Installments
            .OrderBy(i => i.Number)
            .Select(i => new AmortizationEntryDto(
                i.Number,
                i.DueDate,
                i.ScheduledAmount.Amount,
                i.InterestAmount.Amount,
                i.PrincipalAmount.Amount,
                i.RemainingAmount.Amount,
                i.Status.ToString(),
                i.IsOverdue
            ))
            .ToList();

        return Result.Success(
            new LoanDetailDto(
                loan.Id,
                loan.Number.Value,
                loan.CustomerUserId,
                customer is null ? string.Empty : $"{customer.FirstName} {customer.LastName}".Trim(),
                loan.ApprovedPrincipal.Amount,
                loan.AnnualInterestRate.AnnualPercentage,
                loan.TermMonths,
                loan.Installments.First().ScheduledAmount.Amount,
                loan.OutstandingAmount.Amount,
                loan.Status.ToString(),
                loan.IsDelinquent ? "En mora" : "Al día",
                loan.IssuedAt,
                amortization
            )
        );
    }
}
