using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

public sealed class GetMyProductsQueryHandler
    : IRequestHandler<GetMyProductsQuery, Result<MyProductsDto>> {
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly ILoanRepository _loanRepository;
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly IBusinessClock _clock;

    public GetMyProductsQueryHandler(
        ISavingsAccountRepository savingsAccountRepository,
        ILoanRepository loanRepository,
        ICreditCardRepository creditCardRepository,
        ICurrentUserService currentUser,
        IBusinessClock clock
    ) {
        _savingsAccountRepository = savingsAccountRepository;
        _loanRepository = loanRepository;
        _creditCardRepository = creditCardRepository;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async ValueTask<Result<MyProductsDto>> Handle(
        GetMyProductsQuery message,
        CancellationToken cancellationToken
    ) {
        string userId = _currentUser.UserId!;
        var accounts = await _savingsAccountRepository.GetByOwnerAsync(
            userId,
            cancellationToken
        );
        var cards = await _creditCardRepository.GetActiveSummariesByCustomerAsync(
            userId,
            cancellationToken
        );
        Loan? activeLoan = await _loanRepository.GetActiveByCustomerAsync(
            userId,
            cancellationToken
        );
        Loan? loan = activeLoan is null
            ? null
            : await _loanRepository.GetWithInstallmentsByIdAsync(
                activeLoan.Id,
                cancellationToken
            );

        List<MyLoanDto> loans = loan is null
            ? []
            : [
                new MyLoanDto(
                    loan.Number.Value,
                    loan.ApprovedPrincipal.Amount,
                    loan.Installments.Count,
                    loan.Installments.Count(item => item.Status == InstallmentStatus.Paid),
                    loan.OutstandingAmount.Amount,
                    loan.AnnualInterestRate.AnnualPercentage,
                    loan.TermMonths,
                    loan.Installments.Any(item =>
                        item.Status != InstallmentStatus.Paid && item.DueDate < _clock.Today
                    )
                ) { LoanId = loan.Id },
            ];

        return Result.Success(
            new MyProductsDto(
                accounts
                    .Where(account => account.Status == AccountStatus.Active)
                    .OrderBy(account => account.Type != AccountType.Primary)
                    .ThenByDescending(account => account.Balance.Amount)
                    .Select(account => new MyAccountDto(
                        account.Number.Value,
                        account.Balance.Amount,
                        account.Type == AccountType.Primary ? "Principal" : "Secundaria"
                    ))
                    .ToList(),
                loans,
                cards
                    .Select(card => new MyCardDto(
                        card.LastFour,
                        card.CreditLimit,
                        card.AvailableCredit,
                        card.CurrentDebt,
                        card.Expiration
                    ) { CardId = card.Id })
                    .ToList()
            )
        );
    }
}
