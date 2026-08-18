using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using Mediator;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.Handlers;

public sealed class GetAccountTransactionsQueryHandler
    : IRequestHandler<GetAccountTransactionsQuery, Result<AccountDetailDto>> {
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IUserRepository _userRepository;

    public GetAccountTransactionsQueryHandler(
        ISavingsAccountRepository savingsAccountRepository,
        IUserRepository userRepository
    ) {
        _savingsAccountRepository = savingsAccountRepository;
        _userRepository = userRepository;
    }

    public async ValueTask<Result<AccountDetailDto>> Handle(
        GetAccountTransactionsQuery message,
        CancellationToken cancellationToken
    ) {
        Result<AccountNumber> numberResult = AccountNumber.Create(message.AccountNumber);
        if (numberResult.IsFailure) {
            return Result.Failure<AccountDetailDto>(numberResult.Error!);
        }

        SavingsAccount? account = await _savingsAccountRepository.GetByNumberAsync(
            numberResult.Value,
            cancellationToken
        );
        if (account is null) {
            return Result.Failure<AccountDetailDto>(
                DomainError.NotFound("Account.NotFound", "La cuenta de ahorro no existe.")
            );
        }

        UserListDto? customer = await _userRepository.GetByIdAsync(
            account.OwnerUserId,
            cancellationToken
        );
        PageResult<AccountTransactionDto> transactions =
            await _savingsAccountRepository.GetTransactionsPagedAsync(
                numberResult.Value,
                new PageRequest(message.Page, message.PageSize),
                cancellationToken
            );

        return Result.Success(
            new AccountDetailDto(
                account.Number.Value,
                customer is null ? string.Empty : $"{customer.FirstName} {customer.LastName}".Trim(),
                account.Balance.Amount,
                account.Type.ToString(),
                account.Status.ToString(),
                transactions
            )
        );
    }
}
