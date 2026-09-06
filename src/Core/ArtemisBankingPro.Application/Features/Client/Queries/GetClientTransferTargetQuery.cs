using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Queries;

public sealed record GetClientTransferTargetQuery(string DestinationAccountNumber)
    : IRequest<Result<ClientTransferTargetDto>>, IAuthorize {
    public string[] RequiredRoles => ["Cliente"];
}
