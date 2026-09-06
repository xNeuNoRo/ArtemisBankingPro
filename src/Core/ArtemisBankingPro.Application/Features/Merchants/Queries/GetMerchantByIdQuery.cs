using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Merchants.Queries;

/// <summary>
/// Detalle de un comercio por id (spec §40, GET /api/commerce/{id}).
/// </summary>
public sealed record GetMerchantByIdQuery(int MerchantId)
    : IRequest<Result<MerchantDetailDto>>, IAuthorize {
    public string[] RequiredRoles => ["Administrador"];
}
