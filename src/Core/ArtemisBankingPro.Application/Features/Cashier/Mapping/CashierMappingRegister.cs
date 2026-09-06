using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Application.Features.Cashier.Queries;
using ArtemisBankingPro.Application.Features.Cashier.ViewModels;
using ArtemisBankingPro.Domain.Common.Pagination;
using Mapster;
using MapsterMapper;

namespace ArtemisBankingPro.Application.Features.Cashier.Mapping;

public sealed class CashierMappingRegister : IRegister {
    public void Register(TypeAdapterConfig config) {
        config.NewConfig<CashierDashboardDto, CashierDashboardViewModel>();
        config.NewConfig<CashierOperationDto, CashierOperationItemViewModel>();
        config.NewConfig<CashierOperationResponse, TransactionResultViewModel>();
        config.NewConfig<ProcessThirdPartyTransferResponse, ThirdPartyTransferResultViewModel>();

        config.NewConfig<DepositViewModel, ProcessDepositCommand>()
            .MapWith(source => new ProcessDepositCommand(
                source.AccountNumber,
                source.Amount ?? 0m,
                string.Empty
            ));
        config.NewConfig<WithdrawalViewModel, ProcessWithdrawalCommand>()
            .MapWith(source => new ProcessWithdrawalCommand(
                source.AccountNumber,
                source.Amount ?? 0m,
                string.Empty
            ));
        config.NewConfig<CardPaymentViewModel, ProcessCardPaymentCommand>()
            .MapWith(source => new ProcessCardPaymentCommand(
                source.CardId,
                source.AccountNumber,
                source.Amount ?? 0m,
                string.Empty
            ));
        config.NewConfig<LoanPaymentViewModel, ProcessLoanPaymentCommand>()
            .MapWith(source => new ProcessLoanPaymentCommand(
                source.LoanId,
                source.AccountNumber,
                source.Amount ?? 0m,
                string.Empty
            ));
        config.NewConfig<ThirdPartyTransferViewModel, ProcessThirdPartyTransferCommand>()
            .MapWith(source => new ProcessThirdPartyTransferCommand(
                source.SourceAccountNumber,
                source.DestinationAccountNumber,
                source.Amount ?? 0m,
                string.Empty
            ));
        config.NewConfig<CashierOperationListViewModel, GetCashierOperationsQuery>()
            .MapWith(source => new GetCashierOperationsQuery(
                source.DateFrom,
                source.DateTo,
                source.OperationType,
                PageRequest.DefaultPage,
                PageRequest.DefaultPageSize
            ));
    }

    public static CashierOperationListViewModel ToOperationListViewModel(
        IEnumerable<CashierOperationDto> source,
        int page,
        int pageSize,
        int totalCount,
        IMapper mapper,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        string? operationType = null
    ) => new() {
        DateFrom = dateFrom,
        DateTo = dateTo,
        OperationType = operationType,
        Operations = source.Select(mapper.Map<CashierOperationItemViewModel>).ToArray(),
        Pagination = new PaginationViewModel {
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
        },
    };

    public static ProcessDepositCommand ToDepositCommand(
        DepositViewModel source,
        IMapper mapper,
        string idempotencyKey
    ) => mapper.Map<ProcessDepositCommand>(source) with {
        IdempotencyKey = idempotencyKey,
    };

    public static ProcessWithdrawalCommand ToWithdrawalCommand(
        WithdrawalViewModel source,
        IMapper mapper,
        string idempotencyKey
    ) => mapper.Map<ProcessWithdrawalCommand>(source) with {
        IdempotencyKey = idempotencyKey,
    };

    public static ProcessCardPaymentCommand ToCardPaymentCommand(
        CardPaymentViewModel source,
        IMapper mapper,
        string idempotencyKey
    ) => mapper.Map<ProcessCardPaymentCommand>(source) with {
        IdempotencyKey = idempotencyKey,
    };

    public static ProcessLoanPaymentCommand ToLoanPaymentCommand(
        LoanPaymentViewModel source,
        IMapper mapper,
        string idempotencyKey
    ) => mapper.Map<ProcessLoanPaymentCommand>(source) with {
        IdempotencyKey = idempotencyKey,
    };

    public static ProcessThirdPartyTransferCommand ToThirdPartyTransferCommand(
        ThirdPartyTransferViewModel source,
        IMapper mapper,
        string idempotencyKey
    ) => mapper.Map<ProcessThirdPartyTransferCommand>(source) with {
        IdempotencyKey = idempotencyKey,
    };

    public static GetCashierOperationsQuery ToOperationListQuery(
        CashierOperationListViewModel source,
        IMapper mapper,
        int page,
        int pageSize
    ) => mapper.Map<GetCashierOperationsQuery>(source) with {
        Page = page,
        PageSize = pageSize,
    };
}
