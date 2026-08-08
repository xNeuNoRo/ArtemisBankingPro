using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Loans.Commands;

/// <summary>
/// Modifica la tasa de interés anual de un préstamo activo. Solo se recalculan
/// las cuotas futuras pendientes con fecha de vencimiento posterior a la fecha
/// actual; las pagadas, parcialmente pagadas o vencidas no se modifican.
/// </summary>
public sealed record UpdateLoanRateCommand(
    int LoanId,
    decimal AnnualInterestRate
) : IRequest<Result<Unit>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Administrador"];

    public string IdempotencyKey => $"update-loan-rate-{LoanId}";

    public string RequestFingerprint => $"{LoanId}|{AnnualInterestRate}";
}
