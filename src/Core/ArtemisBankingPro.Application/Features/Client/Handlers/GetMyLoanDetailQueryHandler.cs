using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

public sealed class GetMyLoanDetailQueryHandler
    : IRequestHandler<GetMyLoanDetailQuery, Result<MyLoanDetailDto>> {
    private readonly ILoanRepository _loanRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly IBusinessClock _clock;

    public GetMyLoanDetailQueryHandler(
        ILoanRepository loanRepository,
        ICurrentUserService currentUser,
        IBusinessClock clock
    ) {
        _loanRepository = loanRepository;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async ValueTask<Result<MyLoanDetailDto>> Handle(
        GetMyLoanDetailQuery message,
        CancellationToken cancellationToken
    ) {
        Loan? loan = await _loanRepository.GetWithInstallmentsByIdAsync(
            message.LoanId,
            cancellationToken
        );
        if (loan is null) {
            return Result.Failure<MyLoanDetailDto>(
                DomainError.NotFound("Loan.NotFound", "El préstamo indicado no existe.")
            );
        }

        if (loan.CustomerUserId != _currentUser.UserId) {
            throw new ForbiddenAccessException("El préstamo no pertenece al cliente autenticado.");
        }

        return Result.Success(
            new MyLoanDetailDto(
                loan.Id,
                loan.Number.Value,
                loan.ApprovedPrincipal.Amount,
                loan.OutstandingAmount.Amount,
                loan.AnnualInterestRate.AnnualPercentage,
                loan.TermMonths,
                loan.Installments.Count,
                loan.Installments.Count(item => item.Status == InstallmentStatus.Paid),
                loan.Installments.Any(item =>
                    item.Status != InstallmentStatus.Paid && item.DueDate < _clock.Today
                ),
                loan.Installments
                    .OrderBy(item => item.Number)
                    .Select(item => new AmortizationRowDto(
                        item.Number,
                        item.DueDate,
                        item.ScheduledAmount.Amount,
                        item.PaidAmount.Amount,
                        item.Status.ToString(),
                        item.Status != InstallmentStatus.Paid && item.DueDate < _clock.Today
                    ))
                    .ToList()
            )
        );
    }
}
