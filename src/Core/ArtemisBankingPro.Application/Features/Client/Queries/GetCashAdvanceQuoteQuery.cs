using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Queries;

public sealed record GetCashAdvanceQuoteQuery(int CardId, decimal Amount)
    : IRequest<Result<CashAdvanceQuoteDto>>, IAuthorize {
    public string[] RequiredRoles => ["Cliente"];
}
