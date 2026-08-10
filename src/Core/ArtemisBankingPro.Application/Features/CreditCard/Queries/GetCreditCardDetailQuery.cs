using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.CreditCard.Queries;

/// <summary>
/// Detalle de una tarjeta de crédito con sus consumos paginados.
/// </summary>
public sealed record GetCreditCardDetailQuery(
    int CardId,
    int Page = PageRequest.DefaultPage,
    int PageSize = PageRequest.DefaultPageSize
) : IRequest<Result<CreditCardDetailDto>>, IAuthorize {
    public string[] RequiredRoles => ["Administrador"];
}
