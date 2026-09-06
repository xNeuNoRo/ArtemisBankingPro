using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Queries;

public sealed record GetMyLoanDetailQuery(int LoanId)
    : IRequest<Result<MyLoanDetailDto>>, IAuthorize {
    public string[] RequiredRoles => ["Cliente"];
}
