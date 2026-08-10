using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.CreditCard.Queries;

/// <summary>
/// Listado paginado de tarjetas de crédito con filtro por estado y búsqueda
/// por cédula del cliente. Por defecto las activas aparecen primero.
/// </summary>
public sealed record GetCreditCardsPagedQuery(
    int Page = PageRequest.DefaultPage,
    int PageSize = PageRequest.DefaultPageSize,
    string? Status = null,
    string? Identification = null
) : IRequest<Result<PageResult<CreditCardSummaryDto>>>, IAuthorize {
    public string[] RequiredRoles => ["Administrador"];
}
