using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

public sealed class GetMyAccountTransactionsQueryHandler
    : IRequestHandler<
        GetMyAccountTransactionsQuery,
        Result<PageResult<AccountTransactionDto>>
    > {
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly ICurrentUserService _currentUser;

    public GetMyAccountTransactionsQueryHandler(
        ISavingsAccountRepository savingsAccountRepository,
        ICurrentUserService currentUser
    ) {
        _savingsAccountRepository = savingsAccountRepository;
        _currentUser = currentUser;
    }

    public async ValueTask<Result<PageResult<AccountTransactionDto>>> Handle(
        GetMyAccountTransactionsQuery message,
        CancellationToken cancellationToken
    ) {
        Result<AccountNumber> numberResult = AccountNumber.Create(message.AccountNumber);
        if (numberResult.IsFailure) {
            return Result.Failure<PageResult<AccountTransactionDto>>(numberResult.Error!);
        }

        SavingsAccount? account = await _savingsAccountRepository.GetByNumberAsync(
            numberResult.Value,
            cancellationToken
        );
        if (account is null) {
            return Result.Failure<PageResult<AccountTransactionDto>>(
                DomainError.NotFound("Account.NotFound", "La cuenta de ahorro no existe.")
            );
        }

        if (account.OwnerUserId != _currentUser.UserId) {
            throw new ForbiddenAccessException("La cuenta no pertenece al cliente autenticado.");
        }

        List<AccountTransactionDto> transactions = [];
        int sourcePage = PageRequest.DefaultPage;
        while (true) {
            PageResult<AccountTransactionDto> result =
                await _savingsAccountRepository.GetTransactionsPagedAsync(
                    numberResult.Value,
                    new PageRequest(sourcePage, PageRequest.MaxPageSize),
                    cancellationToken
                );
            transactions.AddRange(result.Items);
            if (transactions.Count >= result.TotalCount) {
                break;
            }

            sourcePage++;
        }

        IEnumerable<AccountTransactionDto> filtered = transactions;
        if (message.DateFrom.HasValue) {
            filtered = filtered.Where(item => item.Date >= message.DateFrom.Value);
        }

        if (message.DateTo.HasValue) {
            filtered = filtered.Where(item => item.Date <= message.DateTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(message.TransactionType)) {
            filtered = filtered.Where(item =>
                string.Equals(
                    item.TransactionType,
                    message.TransactionType,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        }

        List<AccountTransactionDto> filteredItems = filtered.ToList();
        var page = new PageRequest(message.Page, message.PageSize);
        return Result.Success(
            new PageResult<AccountTransactionDto>(
                filteredItems.Skip(page.Skip).Take(page.PageSize).ToList(),
                filteredItems.Count,
                page.Page,
                page.PageSize
            )
        );
    }
}
