using System.Globalization;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Commands;

/// <summary>
/// Transfiere fondos entre dos cuentas activas distintas pertenecientes al cliente
/// autenticado, con débito y crédito atómicos.
/// </summary>
public sealed record ProcessOwnAccountsTransferCommand(
    string SourceAccountNumber,
    string DestinationAccountNumber,
    decimal Amount,
    string IdempotencyKey
) : IRequest<Result<Unit>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Cliente"];

    public string RequestFingerprint =>
        $"{SourceAccountNumber}|{DestinationAccountNumber}|{FormatAmount()}";

    private string FormatAmount() => Amount.ToString("0.00", CultureInfo.InvariantCulture);
}
