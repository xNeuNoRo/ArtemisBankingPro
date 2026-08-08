using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Loans.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Loans.Commands;

/// <summary>
/// Asigna un préstamo a un cliente activo: genera el número único de 9 dígitos,
/// la tabla de amortización (sistema francés), evalúa el riesgo de deuda y
/// desembolsa el capital en la cuenta principal del cliente.
/// Si el cliente es o se convierte en alto riesgo y
/// <see cref="ConfirmHighRisk"/> es false, la operación se rechaza con 409.
/// </summary>
public sealed record CreateLoanCommand(
    string CustomerUserId,
    decimal CapitalAmount,
    int TermMonths,
    decimal AnnualInterestRate,
    bool ConfirmHighRisk = false
) : IRequest<Result<CreateLoanResponse>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Administrador"];

    public string IdempotencyKey => $"create-loan-{CustomerUserId}";

    public string RequestFingerprint =>
        $"{CustomerUserId}|{CapitalAmount}|{TermMonths}|{AnnualInterestRate}|{ConfirmHighRisk}";
}
