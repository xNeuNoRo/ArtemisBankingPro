using System.Globalization;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Commands;

/// <summary>
/// Abona a un préstamo propio con fondos de una cuenta propia. El pago se limita
/// al saldo pendiente y se aplica desde la cuota impaga más antigua.
/// </summary>
public sealed record ProcessClientLoanPaymentCommand(
    int LoanId,
    string AccountNumber,
    decimal Amount,
    string IdempotencyKey
) : IRequest<Result<Unit>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Cliente"];

    public string RequestFingerprint => $"{LoanId}|{AccountNumber}|{FormatAmount()}";

    private string FormatAmount() => Amount.ToString("0.00", CultureInfo.InvariantCulture);
}
