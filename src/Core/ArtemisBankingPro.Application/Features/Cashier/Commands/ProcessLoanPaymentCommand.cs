using System.Globalization;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Cashier.Commands;

/// <summary>
/// Pago a préstamo realizado por un cajero (spec §30): debita la cuenta de
/// ahorro indicada y aplica el pago al préstamo, capando el monto efectivo al
/// pendiente real. Atómico: débito de cuenta, aplicación a cuotas, estado del
/// préstamo, transacción y operación financiera.
/// </summary>
public sealed record ProcessLoanPaymentCommand(
    int LoanId,
    string AccountNumber,
    decimal Amount,
    string IdempotencyKey
) : IRequest<Result<CashierOperationResponse>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Cajero"];

    public string RequestFingerprint =>
        $"{LoanId}|{AccountNumber}|{Amount.ToString("0.00", CultureInfo.InvariantCulture)}";
}
