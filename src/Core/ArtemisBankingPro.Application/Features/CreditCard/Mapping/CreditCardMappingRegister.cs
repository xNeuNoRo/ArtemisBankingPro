using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Application.Features.CreditCard.Queries;
using ArtemisBankingPro.Application.Features.CreditCard.ViewModels;
using ArtemisBankingPro.Application.Common.ViewModels;
using Mapster;
using MapsterMapper;
using ArtemisBankingPro.Domain.Common.Pagination;

namespace ArtemisBankingPro.Application.Features.CreditCard.Mapping;

public sealed class CreditCardMappingRegister : IRegister {
    public void Register(TypeAdapterConfig config) {
        config.NewConfig<CreditCardSummaryDto, CreditCardSummaryViewModel>();
        config.NewConfig<CardConsumptionDto, CardConsumptionViewModel>();
        config.NewConfig<CreditCardDetailDto, CreditCardDetailViewModel>()
            .Ignore(destination => destination.Consumptions)
            .Ignore(destination => destination.Pagination);
        config.NewConfig<AssignCreditCardViewModel, AssignCreditCardCommand>()
            .MapWith(source => new AssignCreditCardCommand(
                string.Empty,
                source.CreditLimit ?? 0m
            ));
        config.NewConfig<UpdateCardLimitViewModel, UpdateCardLimitCommand>()
            .MapWith(source => new UpdateCardLimitCommand(
                0,
                source.NewLimit ?? 0m
            ));
        config.NewConfig<CancelCreditCardViewModel, CancelCreditCardCommand>()
            .MapWith(_ => new CancelCreditCardCommand(0));
        config.NewConfig<CreditCardListViewModel, GetCreditCardsPagedQuery>()
            .MapWith(source => new GetCreditCardsPagedQuery(
                PageRequest.DefaultPage,
                PageRequest.DefaultPageSize,
                source.Status,
                source.Identification
            ));
        config.NewConfig<CreditCardDetailViewModel, GetCreditCardDetailQuery>()
            .MapWith(_ => new GetCreditCardDetailQuery(
                0,
                PageRequest.DefaultPage,
                PageRequest.DefaultPageSize
            ));
    }

    public static AssignCreditCardCommand ToAssignCommand(
        AssignCreditCardViewModel source,
        IMapper mapper,
        string customerUserId,
        string idempotencyKey
    ) => mapper.Map<AssignCreditCardCommand>(source) with {
        CustomerUserId = customerUserId,
        IdempotencyKey = idempotencyKey,
    };

    public static UpdateCardLimitCommand ToUpdateLimitCommand(
        UpdateCardLimitViewModel source,
        IMapper mapper,
        int cardId,
        string idempotencyKey
    ) => mapper.Map<UpdateCardLimitCommand>(source) with {
        CardId = cardId,
        IdempotencyKey = idempotencyKey,
    };

    public static CancelCreditCardCommand ToCancelCommand(
        CancelCreditCardViewModel source,
        IMapper mapper,
        int cardId,
        string idempotencyKey
    ) => mapper.Map<CancelCreditCardCommand>(source) with {
        CardId = cardId,
        IdempotencyKey = idempotencyKey,
    };

    public static GetCreditCardsPagedQuery ToListQuery(
        CreditCardListViewModel source,
        IMapper mapper,
        int page,
        int pageSize
    ) => mapper.Map<GetCreditCardsPagedQuery>(source) with {
        Page = page,
        PageSize = pageSize,
    };

    public static CreditCardListViewModel ToListViewModel(
        IEnumerable<CreditCardSummaryDto> cards,
        int page,
        int pageSize,
        int totalCount,
        IMapper mapper,
        string? status = null,
        string? identification = null
    ) => new() {
        Status = status,
        Identification = identification,
        Cards = cards.Select(mapper.Map<CreditCardSummaryViewModel>).ToArray(),
        Pagination = new PaginationViewModel {
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
        },
    };

    public static CreditCardDetailViewModel ToDetailViewModel(
        CreditCardDetailDto source,
        IMapper mapper
    ) => new() {
        Id = source.Id,
        MaskedNumber = source.MaskedNumber,
        LastFour = source.LastFour,
        ClientFullName = source.ClientFullName,
        CreditLimit = source.CreditLimit,
        AvailableCredit = source.AvailableCredit,
        CurrentDebt = source.CurrentDebt,
        Expiration = source.Expiration,
        Status = source.Status,
        Consumptions = source.Consumptions.Items
            .Select(mapper.Map<CardConsumptionViewModel>)
            .ToArray(),
        Pagination = new PaginationViewModel {
            Page = source.Consumptions.Page,
            PageSize = source.Consumptions.PageSize,
            TotalItems = source.Consumptions.TotalCount,
        },
    };

    public static GetCreditCardDetailQuery ToDetailQuery(
        CreditCardDetailViewModel source,
        IMapper mapper,
        int cardId,
        int page,
        int pageSize
    ) => mapper.Map<GetCreditCardDetailQuery>(source) with {
        CardId = cardId,
        Page = page,
        PageSize = pageSize,
    };
}
