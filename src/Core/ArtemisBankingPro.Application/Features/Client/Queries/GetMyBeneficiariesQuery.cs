using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Queries;

public sealed record GetMyBeneficiariesQuery
    : IRequest<Result<IReadOnlyList<MyBeneficiaryDto>>>, IAuthorize {
    public string[] RequiredRoles => ["Cliente"];
}
