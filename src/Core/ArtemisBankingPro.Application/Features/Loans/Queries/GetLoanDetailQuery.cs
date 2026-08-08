using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Loans.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Loans.Queries;

/// <summary>
/// Detalle de un préstamo con su tabla de amortización completa.
/// </summary>
public sealed record GetLoanDetailQuery(int LoanId)
    : IRequest<Result<LoanDetailDto>>, IAuthorize
{
    public string[] RequiredRoles => ["Administrador"];
}
