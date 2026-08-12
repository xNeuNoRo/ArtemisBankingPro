using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.HermesPay.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.HermesPay.Queries;

/// <summary>
/// Consulta las transacciones recibidas por un comercio vía Hermes Pay (spec
/// §41, GET /pay/get-transactions/{commerceId}). Con rol Comercio el
/// commerceId se obtiene del JWT y se ignora el valor recibido; con rol
/// Administrador se usa el commerceId del request. Paginada y ordenada de la
/// más reciente a la más antigua.
/// </summary>
public sealed record GetCommerceTransactionsQuery(
    int? CommerceId = null,
    int Page = PageRequest.DefaultPage,
    int PageSize = PageRequest.DefaultPageSize
) : IRequest<Result<GetCommerceTransactionsResponseDto>>, IAuthorize {
    public string[] RequiredRoles => ["Comercio", "Administrador"];
}
