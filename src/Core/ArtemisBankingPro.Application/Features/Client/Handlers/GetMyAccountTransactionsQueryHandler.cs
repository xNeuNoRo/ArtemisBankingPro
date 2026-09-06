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

        PageResult<AccountTransactionDto> result =
            await _savingsAccountRepository.GetTransactionsPagedAsync(
                numberResult.Value,
                new PageRequest(message.Page, message.PageSize),
                message.DateFrom,
                message.DateTo,
                message.TransactionType,
                cancellationToken
            );
        return Result.Success(result);
    }
}
