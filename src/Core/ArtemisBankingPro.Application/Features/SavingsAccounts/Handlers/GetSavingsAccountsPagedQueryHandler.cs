using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using Mediator;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.Handlers;

public sealed class GetSavingsAccountsPagedQueryHandler
    : IRequestHandler<
        GetSavingsAccountsPagedQuery,
        Result<PageResult<SavingsAccountSummaryDto>>
    > {
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IUserRepository _userRepository;

    public GetSavingsAccountsPagedQueryHandler(
        ISavingsAccountRepository savingsAccountRepository,
        IUserRepository userRepository
    ) {
        _savingsAccountRepository = savingsAccountRepository;
        _userRepository = userRepository;
    }

    public async ValueTask<Result<PageResult<SavingsAccountSummaryDto>>> Handle(
        GetSavingsAccountsPagedQuery message,
        CancellationToken cancellationToken
    ) {
        string? customerUserId = null;
        if (!string.IsNullOrWhiteSpace(message.Identification)) {
            UserListDto? customer = await _userRepository.GetByIdentityDocumentAsync(
                message.Identification,
                cancellationToken
            );
            if (customer is null) {
                return Result.Failure<PageResult<SavingsAccountSummaryDto>>(
                    DomainError.NotFound(
                        "Account.CustomerNotFound",
                        "No existe un cliente registrado con esta cédula."
                    )
                );
            }

            customerUserId = customer.Id;
        }

        AccountStatus? status = message.Status?.ToLowerInvariant() switch {
            "activa" => AccountStatus.Active,
            "cancelada" => AccountStatus.Cancelled,
            _ => null,
        };
        AccountType? type = message.Type?.ToLowerInvariant() switch {
            "principal" => AccountType.Primary,
            "secundaria" => AccountType.Secondary,
            _ => null,
        };

        var page = new PageRequest(message.Page, message.PageSize);
        PageResult<SavingsAccountSummaryDto> paged =
            await _savingsAccountRepository.GetPagedAsync(
                customerUserId,
                status,
                type,
                page,
                cancellationToken
            );

        IReadOnlyList<UserListDto> customers = await _userRepository.GetByIdsAsync(
            paged.Items.Select(account => account.ClientId).Distinct().ToList(),
            cancellationToken
        );
        Dictionary<string, UserListDto> customerMap = customers.ToDictionary(customer => customer.Id);
        List<SavingsAccountSummaryDto> items = paged.Items
            .Select(account =>
                customerMap.TryGetValue(account.ClientId, out UserListDto? customer)
                    ? account with {
                        ClientFullName = $"{customer.FirstName} {customer.LastName}".Trim(),
                        Identification = customer.Identification,
                    }
                    : account
            )
            .ToList();

        return Result.Success(
            new PageResult<SavingsAccountSummaryDto>(
                items,
                paged.TotalCount,
                page.Page,
                page.PageSize
            )
        );
    }
}
