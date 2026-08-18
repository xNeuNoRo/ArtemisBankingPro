using ArtemisBankingPro.Application.Features.Loans.Commands;
using ArtemisBankingPro.Application.Features.Loans.DTOs;
using ArtemisBankingPro.Application.Features.Loans.Queries;
using ArtemisBankingPro.Application.Features.Loans.ViewModels;
using ArtemisBankingPro.Application.Common.ViewModels;
using Mapster;
using MapsterMapper;
using ArtemisBankingPro.Domain.Common.Pagination;

namespace ArtemisBankingPro.Application.Features.Loans.Mapping;

public sealed class LoansMappingRegister : IRegister {
    public void Register(TypeAdapterConfig config) {
        config.NewConfig<LoanListDto, LoanListItemViewModel>();
        config.NewConfig<AmortizationEntryDto, LoanInstallmentViewModel>();
        config.NewConfig<LoanDetailDto, LoanDetailViewModel>()
            .Ignore(destination => destination.Amortization);
        config.NewConfig<CreateLoanViewModel, CreateLoanCommand>()
            .MapWith(source => new CreateLoanCommand(
                string.Empty,
                source.CapitalAmount ?? 0m,
                source.TermMonths ?? 0,
                source.AnnualInterestRate ?? 0m,
                false
            ));
        config.NewConfig<UpdateLoanRateViewModel, UpdateLoanRateCommand>()
            .MapWith(source => new UpdateLoanRateCommand(
                0,
                source.AnnualInterestRate ?? 0m
            ));
        config.NewConfig<LoanListViewModel, GetLoansPagedQuery>()
            .MapWith(source => new GetLoansPagedQuery(
                PageRequest.DefaultPage,
                PageRequest.DefaultPageSize,
                source.Status,
                source.Identification
            ));
        config.NewConfig<LoanDetailViewModel, GetLoanDetailQuery>()
            .MapWith(_ => new GetLoanDetailQuery(0));
    }

    public static CreateLoanCommand ToCreateCommand(
        CreateLoanViewModel source,
        IMapper mapper,
        string customerUserId,
        bool confirmHighRisk,
        string idempotencyKey
    ) => mapper.Map<CreateLoanCommand>(source) with {
        CustomerUserId = customerUserId,
        ConfirmHighRisk = confirmHighRisk,
        IdempotencyKey = idempotencyKey,
    };

    public static UpdateLoanRateCommand ToUpdateRateCommand(
        UpdateLoanRateViewModel source,
        IMapper mapper,
        int loanId,
        string idempotencyKey
    ) => mapper.Map<UpdateLoanRateCommand>(source) with {
        LoanId = loanId,
        IdempotencyKey = idempotencyKey,
    };

    public static GetLoansPagedQuery ToListQuery(
        LoanListViewModel source,
        IMapper mapper,
        int page,
        int pageSize
    ) => mapper.Map<GetLoansPagedQuery>(source) with {
        Page = page,
        PageSize = pageSize,
    };

    public static LoanListViewModel ToListViewModel(
        IEnumerable<LoanListDto> loans,
        int page,
        int pageSize,
        int totalCount,
        IMapper mapper,
        string? status = null,
        string? identification = null
    ) => new() {
        Status = status,
        Identification = identification,
        Loans = loans.Select(mapper.Map<LoanListItemViewModel>).ToArray(),
        Pagination = new PaginationViewModel {
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
        },
    };

    public static GetLoanDetailQuery ToDetailQuery(
        LoanDetailViewModel source,
        IMapper mapper,
        int loanId
    ) => mapper.Map<GetLoanDetailQuery>(source) with {
        LoanId = loanId,
    };
}
