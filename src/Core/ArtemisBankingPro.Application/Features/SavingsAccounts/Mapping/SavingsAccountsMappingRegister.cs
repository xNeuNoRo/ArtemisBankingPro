using ArtemisBankingPro.Application.Features.SavingsAccounts.Commands;
using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Queries;
using ArtemisBankingPro.Application.Features.SavingsAccounts.ViewModels;
using ArtemisBankingPro.Application.Common.ViewModels;
using Mapster;
using MapsterMapper;
using ArtemisBankingPro.Domain.Common.Pagination;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.Mapping;

public sealed class SavingsAccountsMappingRegister : IRegister {
    public void Register(TypeAdapterConfig config) {
        config.NewConfig<SavingsAccountSummaryDto, SavingsAccountSummaryViewModel>();
        config.NewConfig<AccountTransactionDto, AccountTransactionItemViewModel>();
        config.NewConfig<AssignSecondaryAccountViewModel, AssignSecondarySavingsAccountCommand>()
            .MapWith(source => new AssignSecondarySavingsAccountCommand(
                string.Empty,
                source.InitialAmount ?? 0m
            ));
        config.NewConfig<CancelSecondaryAccountViewModel, CancelSecondarySavingsAccountCommand>()
            .MapWith(_ => new CancelSecondarySavingsAccountCommand(string.Empty));
        config.NewConfig<SavingsAccountListViewModel, GetSavingsAccountsPagedQuery>()
            .MapWith(source => new GetSavingsAccountsPagedQuery(
                PageRequest.DefaultPage,
                PageRequest.DefaultPageSize,
                source.Status,
                source.Type,
                source.Identification
            ));
        config.NewConfig<AccountDetailViewModel, GetAccountTransactionsQuery>()
            .MapWith(_ => new GetAccountTransactionsQuery(
                string.Empty,
                PageRequest.DefaultPage,
                PageRequest.DefaultPageSize
            ));
    }

    public static AssignSecondarySavingsAccountCommand ToAssignCommand(
        AssignSecondaryAccountViewModel source,
        IMapper mapper,
        string customerUserId,
        string idempotencyKey
    ) => mapper.Map<AssignSecondarySavingsAccountCommand>(source) with {
        CustomerUserId = customerUserId,
        IdempotencyKey = idempotencyKey,
    };

    public static CancelSecondarySavingsAccountCommand ToCancelCommand(
        CancelSecondaryAccountViewModel source,
        IMapper mapper,
        string accountNumber,
        string idempotencyKey
    ) => mapper.Map<CancelSecondarySavingsAccountCommand>(source) with {
        AccountNumber = accountNumber,
        IdempotencyKey = idempotencyKey,
    };

    public static GetSavingsAccountsPagedQuery ToListQuery(
        SavingsAccountListViewModel source,
        IMapper mapper,
        int page,
        int pageSize
    ) => mapper.Map<GetSavingsAccountsPagedQuery>(source) with {
        Page = page,
        PageSize = pageSize,
    };

    public static SavingsAccountListViewModel ToListViewModel(
        IEnumerable<SavingsAccountSummaryDto> accounts,
        int page,
        int pageSize,
        int totalCount,
        IMapper mapper,
        string? status = null,
        string? type = null,
        string? identification = null
    ) => new() {
        Status = status,
        Type = type,
        Identification = identification,
        StatusOptions = SavingsAccountListViewModel.BuildStatusOptions(
            status,
            !string.IsNullOrWhiteSpace(identification)
        ),
        TypeOptions = SavingsAccountListViewModel.BuildTypeOptions(type),
        Accounts = accounts.Select(mapper.Map<SavingsAccountSummaryViewModel>).ToArray(),
        Pagination = new PaginationViewModel {
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
        },
    };

    public static AccountDetailViewModel ToDetailViewModel(
        AccountDetailDto source,
        IMapper mapper
    ) => new() {
        AccountNumber = source.AccountNumber,
        ClientFullName = source.ClientFullName,
        Balance = source.Balance,
        Type = source.Type,
        Status = source.Status,
        Transactions = source.Transactions.Items
            .Select(mapper.Map<AccountTransactionItemViewModel>)
            .ToArray(),
        Pagination = new PaginationViewModel {
            Page = source.Transactions.Page,
            PageSize = source.Transactions.PageSize,
            TotalItems = source.Transactions.TotalCount,
        },
    };

    public static GetAccountTransactionsQuery ToDetailQuery(
        AccountDetailViewModel source,
        IMapper mapper,
        string accountNumber,
        int page,
        int pageSize
    ) => mapper.Map<GetAccountTransactionsQuery>(source) with {
        AccountNumber = accountNumber,
        Page = page,
        PageSize = pageSize,
    };
}
