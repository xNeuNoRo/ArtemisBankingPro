using ArtemisBankingPro.Application.Features.Merchants.Commands;
using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Application.Features.Merchants.Queries;
using ArtemisBankingPro.Application.Features.Merchants.ViewModels;
using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Merchants.Enums;
using ArtemisBankingPro.Domain.Common.Pagination;
using Mapster;
using MapsterMapper;

namespace ArtemisBankingPro.Application.Features.Merchants.Mapping;

public sealed class MerchantsMappingRegister : IRegister {
    public void Register(TypeAdapterConfig config) {
        config.NewConfig<Merchant, MerchantDetailDto>()
            .Map(dest => dest.IsActive, src => src.Status == MerchantStatus.Active);
        config.NewConfig<Merchant, MerchantSummaryDto>()
            .Map(dest => dest.IsActive, src => src.Status == MerchantStatus.Active)
            .Map(dest => dest.HasAssociatedUser, src => !string.IsNullOrWhiteSpace(src.AssociatedUserId));
        config.NewConfig<MerchantSummaryDto, MerchantSummaryViewModel>();
        config.NewConfig<MerchantUserDto, MerchantUserViewModel>();
        config.NewConfig<MerchantDetailDto, MerchantDetailViewModel>();
        config.NewConfig<CreateMerchantViewModel, CreateMerchantCommand>();
        config.NewConfig<UpdateMerchantViewModel, UpdateMerchantCommand>()
            .MapWith(source => new UpdateMerchantCommand(
                0,
                source.Name,
                source.Description,
                source.Email,
                source.PhoneNumber,
                source.Rnc
            ));
        config.NewConfig<ChangeMerchantStatusViewModel, ChangeMerchantStatusCommand>()
            .MapWith(source => new ChangeMerchantStatusCommand(0, source.IsActive));
        config.NewConfig<AssignCommerceUserViewModel, CreateCommerceUserCommand>()
            .MapWith(source => new CreateCommerceUserCommand(
                0,
                source.FirstName,
                source.LastName,
                source.Identification,
                source.Email,
                source.UserName,
                source.Password,
                source.ConfirmPassword,
                source.InitialAmount,
                null
            ));
        config.NewConfig<MerchantListViewModel, GetMerchantsPagedQuery>()
            .MapWith(source => new GetMerchantsPagedQuery(
                PageRequest.DefaultPage,
                PageRequest.DefaultPageSize,
                source.Status
            ));
        config.NewConfig<MerchantDetailViewModel, GetMerchantByIdQuery>()
            .MapWith(_ => new GetMerchantByIdQuery(0));
    }

    public static CreateMerchantCommand ToCreateCommand(
        CreateMerchantViewModel source,
        IMapper mapper,
        string idempotencyKey
    ) => mapper.Map<CreateMerchantCommand>(source) with {
        IdempotencyKey = idempotencyKey,
    };

    public static UpdateMerchantCommand ToUpdateCommand(
        UpdateMerchantViewModel source,
        IMapper mapper,
        int merchantId,
        string idempotencyKey
    ) => mapper.Map<UpdateMerchantCommand>(source) with {
        MerchantId = merchantId,
        IdempotencyKey = idempotencyKey,
    };

    public static ChangeMerchantStatusCommand ToChangeStatusCommand(
        ChangeMerchantStatusViewModel source,
        IMapper mapper,
        int merchantId,
        string idempotencyKey
    ) => mapper.Map<ChangeMerchantStatusCommand>(source) with {
        MerchantId = merchantId,
        IdempotencyKey = idempotencyKey,
    };

    public static CreateCommerceUserCommand ToAssignCommerceUserCommand(
        AssignCommerceUserViewModel source,
        IMapper mapper,
        int commerceId,
        string idempotencyKey,
        string? callbackUrl
    ) => mapper.Map<CreateCommerceUserCommand>(source) with {
        CommerceId = commerceId,
        IdempotencyKey = idempotencyKey,
        CallbackUrl = callbackUrl,
    };

    public static GetMerchantsPagedQuery ToListQuery(
        MerchantListViewModel source,
        IMapper mapper,
        int page,
        int pageSize
    ) => mapper.Map<GetMerchantsPagedQuery>(source) with {
        Page = page,
        PageSize = pageSize,
    };

    public static GetMerchantByIdQuery ToDetailQuery(
        MerchantDetailViewModel source,
        IMapper mapper,
        int merchantId
    ) => mapper.Map<GetMerchantByIdQuery>(source) with {
        MerchantId = merchantId,
    };

    public static MerchantListViewModel ToListViewModel(
        GetMerchantsPagedResponseDto source,
        IMapper mapper,
        string? status = null
    ) => new() {
        Status = status,
        Merchants = source.Data.Select(mapper.Map<MerchantSummaryViewModel>).ToArray(),
        Pagination = new PaginationViewModel {
            Page = source.Page,
            PageSize = source.PageSize,
            TotalItems = source.TotalRecords,
        },
    };
}
