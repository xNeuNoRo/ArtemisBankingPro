using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.CreditCard.Queries;

/// <summary>
/// API read model. Its consumption collection is intentionally not a public
/// PageResult; that shape is fixed by ADR-022.
/// </summary>
public sealed record GetCreditCardApiDetailQuery(int CardId)
    : IRequest<Result<CreditCardApiDetailDto>>, IAuthorize {
    public string[] RequiredRoles => ["Administrador"];
}
