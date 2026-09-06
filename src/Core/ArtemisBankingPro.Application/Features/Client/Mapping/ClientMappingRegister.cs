using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Features.Client.ViewModels;
using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using Mapster;
using MapsterMapper;

namespace ArtemisBankingPro.Application.Features.Client.Mapping;

public sealed class ClientMappingRegister : IRegister {
    public void Register(TypeAdapterConfig config) {
        config.NewConfig<MyAccountDto, MyAccountViewModel>();
        config.NewConfig<MyLoanDto, MyLoanViewModel>();
        config.NewConfig<MyCardDto, MyCardViewModel>();
        config.NewConfig<MyProductsDto, MyProductsViewModel>()
            .Ignore(destination => destination.Accounts)
            .Ignore(destination => destination.Loans)
            .Ignore(destination => destination.Cards);
        config.NewConfig<AccountTransactionDto, ClientAccountTransactionItemViewModel>();
        config.NewConfig<MyLoanDetailDto, MyLoanDetailViewModel>()
            .Ignore(destination => destination.Amortization);
        config.NewConfig<AmortizationRowDto, MyLoanInstallmentViewModel>();
        config.NewConfig<MyCardDetailDto, MyCardDetailViewModel>()
            .Ignore(destination => destination.Consumptions)
            .Ignore(destination => destination.Pagination);
        config.NewConfig<MyCardConsumptionDto, MyCardConsumptionViewModel>();
        config.NewConfig<MyBeneficiaryDto, BeneficiaryItemViewModel>();
        config.NewConfig<CashAdvanceQuoteDto, CashAdvanceQuoteViewModel>();

        config.NewConfig<AddBeneficiaryViewModel, AddBeneficiaryCommand>()
            .MapWith(source => new AddBeneficiaryCommand(source.DestinationAccountNumber));
        config.NewConfig<RemoveBeneficiaryViewModel, RemoveBeneficiaryCommand>()
            .MapWith(_ => new RemoveBeneficiaryCommand(0));
        config.NewConfig<ExpressTransactionViewModel, ProcessExpressTransactionCommand>()
            .MapWith(source => new ProcessExpressTransactionCommand(
                source.SourceAccountNumber,
                source.DestinationAccountNumber,
                source.Amount ?? 0m,
                string.Empty
            ));
        config.NewConfig<BeneficiaryTransferViewModel, ProcessBeneficiaryTransferCommand>()
            .MapWith(source => new ProcessBeneficiaryTransferCommand(
                source.BeneficiaryId,
                source.SourceAccountNumber,
                source.Amount ?? 0m,
                string.Empty
            ));
        config.NewConfig<OwnAccountsTransferViewModel, ProcessOwnAccountsTransferCommand>()
            .MapWith(source => new ProcessOwnAccountsTransferCommand(
                source.SourceAccountNumber,
                source.DestinationAccountNumber,
                source.Amount ?? 0m,
                string.Empty
            ));
        config.NewConfig<ClientCardPaymentViewModel, ProcessClientCardPaymentCommand>()
            .MapWith(source => new ProcessClientCardPaymentCommand(
                source.CardId,
                source.AccountNumber,
                source.Amount ?? 0m,
                string.Empty
            ));
        config.NewConfig<ClientLoanPaymentViewModel, ProcessClientLoanPaymentCommand>()
            .MapWith(source => new ProcessClientLoanPaymentCommand(
                source.LoanId,
                source.AccountNumber,
                source.Amount ?? 0m,
                string.Empty
            ));
        config.NewConfig<CashAdvanceViewModel, ProcessCashAdvanceCommand>()
            .MapWith(source => new ProcessCashAdvanceCommand(
                source.CardId,
                source.DestinationAccountNumber,
                source.Amount ?? 0m,
                string.Empty
            ));

        config.NewConfig<MyAccountTransactionsViewModel, GetMyAccountTransactionsQuery>()
            .MapWith(source => new GetMyAccountTransactionsQuery(
                string.Empty,
                source.DateFrom,
                source.DateTo,
                source.TransactionType,
                PageRequest.DefaultPage,
                PageRequest.DefaultPageSize
            ));
        config.NewConfig<MyLoanDetailViewModel, GetMyLoanDetailQuery>()
            .MapWith(_ => new GetMyLoanDetailQuery(0));
        config.NewConfig<MyCardDetailViewModel, GetMyCardDetailQuery>()
            .MapWith(_ => new GetMyCardDetailQuery(
                0,
                PageRequest.DefaultPage,
                PageRequest.DefaultPageSize
            ));
        config.NewConfig<CashAdvanceQuoteViewModel, GetCashAdvanceQuoteQuery>()
            .MapWith(source => new GetCashAdvanceQuoteQuery(
                source.CardId,
                source.Amount ?? 0m
            ));
    }

    public static ClientDashboardViewModel ToDashboardViewModel(
        MyProductsDto source,
        IMapper mapper
    ) => new() {
        Products = ToProductsViewModel(source, mapper),
    };

    public static MyProductsViewModel ToProductsViewModel(
        MyProductsDto source,
        IMapper mapper
    ) => new() {
        Accounts = source.Accounts.Select(mapper.Map<MyAccountViewModel>).ToArray(),
        Loans = source.Loans.Select(mapper.Map<MyLoanViewModel>).ToArray(),
        Cards = source.Cards.Select(mapper.Map<MyCardViewModel>).ToArray(),
    };

    public static MyAccountTransactionsViewModel ToAccountTransactionsViewModel(
        string accountNumber,
        IEnumerable<AccountTransactionDto> transactions,
        int page,
        int pageSize,
        int totalCount,
        IMapper mapper,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        string? transactionType = null
    ) => new() {
        AccountNumber = accountNumber,
        DateFrom = dateFrom,
        DateTo = dateTo,
        TransactionType = transactionType,
        Transactions = transactions.Select(mapper.Map<ClientAccountTransactionItemViewModel>).ToArray(),
        Pagination = new PaginationViewModel {
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
        },
    };

    public static MyLoanDetailViewModel ToLoanDetailViewModel(
        MyLoanDetailDto source,
        IMapper mapper
    ) => new() {
        LoanId = source.LoanId,
        LoanNumber = source.LoanNumber,
        ApprovedPrincipal = source.ApprovedPrincipal,
        OutstandingAmount = source.OutstandingAmount,
        AnnualRate = source.AnnualRate,
        TermMonths = source.TermMonths,
        TotalInstallments = source.TotalInstallments,
        PaidInstallments = source.PaidInstallments,
        IsDelinquent = source.IsDelinquent,
        Amortization = source.Amortization
            .Select(mapper.Map<MyLoanInstallmentViewModel>)
            .ToArray(),
    };

    public static MyCardDetailViewModel ToCardDetailViewModel(
        MyCardDetailDto source,
        IMapper mapper
    ) => new() {
        CardId = source.CardId,
        LastFour = source.LastFour,
        CreditLimit = source.CreditLimit,
        AvailableCredit = source.AvailableCredit,
        CurrentDebt = source.CurrentDebt,
        Expiration = source.Expiration,
        Consumptions = source.Consumptions.Items
            .Select(mapper.Map<MyCardConsumptionViewModel>)
            .ToArray(),
        Pagination = new PaginationViewModel {
            Page = source.Consumptions.Page,
            PageSize = source.Consumptions.PageSize,
            TotalItems = source.Consumptions.TotalCount,
        },
    };

    public static BeneficiaryListViewModel ToBeneficiaryListViewModel(
        IEnumerable<MyBeneficiaryDto> source,
        IMapper mapper
    ) => new() {
        Beneficiaries = source.Select(mapper.Map<BeneficiaryItemViewModel>).ToArray(),
    };

    public static CashAdvanceQuoteViewModel ToCashAdvanceQuoteViewModel(
        CashAdvanceQuoteDto source,
        int cardId,
        decimal amount,
        IMapper mapper
    ) {
        CashAdvanceQuoteViewModel viewModel = mapper.Map<CashAdvanceQuoteViewModel>(source);
        viewModel.CardId = cardId;
        viewModel.Amount = amount;
        return viewModel;
    }

    public static AddBeneficiaryCommand ToAddBeneficiaryCommand(
        AddBeneficiaryViewModel source,
        IMapper mapper,
        string idempotencyKey
    ) => mapper.Map<AddBeneficiaryCommand>(source) with {
        IdempotencyKey = idempotencyKey,
    };

    public static RemoveBeneficiaryCommand ToRemoveBeneficiaryCommand(
        RemoveBeneficiaryViewModel _,
        int beneficiaryId,
        string idempotencyKey
    ) => new RemoveBeneficiaryCommand(beneficiaryId) {
        IdempotencyKey = idempotencyKey,
    };

    public static ProcessExpressTransactionCommand ToExpressTransactionCommand(
        ExpressTransactionViewModel source,
        IMapper mapper,
        string idempotencyKey
    ) => mapper.Map<ProcessExpressTransactionCommand>(source) with {
        IdempotencyKey = idempotencyKey,
    };

    public static ProcessBeneficiaryTransferCommand ToBeneficiaryTransferCommand(
        BeneficiaryTransferViewModel source,
        IMapper mapper,
        string idempotencyKey
    ) => mapper.Map<ProcessBeneficiaryTransferCommand>(source) with {
        IdempotencyKey = idempotencyKey,
    };

    public static ProcessOwnAccountsTransferCommand ToOwnAccountsTransferCommand(
        OwnAccountsTransferViewModel source,
        IMapper mapper,
        string idempotencyKey
    ) => mapper.Map<ProcessOwnAccountsTransferCommand>(source) with {
        IdempotencyKey = idempotencyKey,
    };

    public static ProcessClientCardPaymentCommand ToClientCardPaymentCommand(
        ClientCardPaymentViewModel source,
        IMapper mapper,
        string idempotencyKey
    ) => mapper.Map<ProcessClientCardPaymentCommand>(source) with {
        IdempotencyKey = idempotencyKey,
    };

    public static ProcessClientLoanPaymentCommand ToClientLoanPaymentCommand(
        ClientLoanPaymentViewModel source,
        IMapper mapper,
        string idempotencyKey
    ) => mapper.Map<ProcessClientLoanPaymentCommand>(source) with {
        IdempotencyKey = idempotencyKey,
    };

    public static ProcessCashAdvanceCommand ToCashAdvanceCommand(
        CashAdvanceViewModel source,
        IMapper mapper,
        string idempotencyKey
    ) => mapper.Map<ProcessCashAdvanceCommand>(source) with {
        IdempotencyKey = idempotencyKey,
    };

    public static GetMyAccountTransactionsQuery ToAccountTransactionsQuery(
        MyAccountTransactionsViewModel source,
        IMapper mapper,
        string accountNumber,
        int page,
        int pageSize
    ) => mapper.Map<GetMyAccountTransactionsQuery>(source) with {
        AccountNumber = accountNumber,
        Page = page,
        PageSize = pageSize,
    };

    public static GetMyCardDetailQuery ToCardDetailQuery(
        MyCardDetailViewModel source,
        IMapper mapper,
        int cardId,
        int page,
        int pageSize
    ) => mapper.Map<GetMyCardDetailQuery>(source) with {
        CardId = cardId,
        Page = page,
        PageSize = pageSize,
    };

    public static GetMyLoanDetailQuery ToLoanDetailQuery(
        MyLoanDetailViewModel source,
        IMapper mapper,
        int loanId
    ) => mapper.Map<GetMyLoanDetailQuery>(source) with {
        LoanId = loanId,
    };
}
