using ArtemisBankingPro.Application.Features.Admin.DTOs;
using ArtemisBankingPro.Application.Features.Admin.Queries;
using ArtemisBankingPro.Application.Features.Admin.ViewModels;
using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Domain.Common.Pagination;
using Mapster;
using MapsterMapper;

namespace ArtemisBankingPro.Application.Features.Admin.Mapping;

public sealed class AdminMappingRegister : IRegister {
    public void Register(TypeAdapterConfig config) {
        config.NewConfig<AdminDashboardDto, AdminDashboardViewModel>();
        config.NewConfig<EligibleClientDto, EligibleClientItemViewModel>();
        config.NewConfig<EligibleClientsViewModel, GetEligibleClientsQuery>()
            .MapWith(source => new GetEligibleClientsQuery(
                ClientAssignmentProduct.Loan,
                PageRequest.DefaultPage,
                PageRequest.DefaultPageSize,
                source.Identification
            ));
    }

    public static GetEligibleClientsQuery ToQuery(
        EligibleClientsViewModel source,
        IMapper mapper,
        ClientAssignmentProduct product,
        int page,
        int pageSize
    ) => mapper.Map<GetEligibleClientsQuery>(source) with {
        Product = product,
        Page = page,
        PageSize = pageSize,
    };

    public static EligibleClientsViewModel ToViewModel(
        EligibleClientsResponse source,
        IMapper mapper
    ) => new() {
        AverageDebt = source.AverageDebt,
        Clients = source.Clients.Items.Select(mapper.Map<EligibleClientItemViewModel>).ToArray(),
        Pagination = new PaginationViewModel {
            Page = source.Clients.Page,
            PageSize = source.Clients.PageSize,
            TotalItems = source.Clients.TotalCount,
        },
    };
}
