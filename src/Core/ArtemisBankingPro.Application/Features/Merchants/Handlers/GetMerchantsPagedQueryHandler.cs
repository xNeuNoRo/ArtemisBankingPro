using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Application.Features.Merchants.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Merchants.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Merchants.Handlers;

/// <summary>
/// Lista comercios paginados (spec §40, GET /api/commerce). Si no se envía
/// status se devuelven solo los activos; "todos" omite el filtro. Los valores
/// inválidos los bloquea el validator.
/// </summary>
public sealed class GetMerchantsPagedQueryHandler
    : IRequestHandler<GetMerchantsPagedQuery, Result<GetMerchantsPagedResponseDto>> {
    private readonly IMerchantRepository _merchantRepository;

    public GetMerchantsPagedQueryHandler(IMerchantRepository merchantRepository) {
        _merchantRepository = merchantRepository;
    }

    public async ValueTask<Result<GetMerchantsPagedResponseDto>> Handle(
        GetMerchantsPagedQuery message,
        CancellationToken cancellationToken
    ) {
        MerchantStatus? status;
        switch (message.Status) {
            case null:
            case "activo":
                status = MerchantStatus.Active;
                break;
            case "inactivo":
                status = MerchantStatus.Inactive;
                break;
            default:
                // "todos": sin filtro de estado.
                status = null;
                break;
        }

        var page = new PageRequest(message.Page, message.PageSize);
        PageResult<MerchantSummaryDto> paged = await _merchantRepository.GetPagedAsync(
            status,
            page,
            cancellationToken
        );

        return Result.Success(
            new GetMerchantsPagedResponseDto(
                paged.Page,
                paged.PageSize,
                paged.TotalCount,
                paged.TotalPages,
                paged.Items
            )
        );
    }
}
