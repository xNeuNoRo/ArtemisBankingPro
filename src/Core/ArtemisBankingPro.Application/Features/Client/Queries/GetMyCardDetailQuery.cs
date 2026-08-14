using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Queries;

public sealed record GetMyCardDetailQuery(
    int CardId,
    int Page = PageRequest.DefaultPage,
    int PageSize = PageRequest.DefaultPageSize
) : IRequest<Result<MyCardDetailDto>>, IAuthorize {
    public string[] RequiredRoles => ["Cliente"];
}
