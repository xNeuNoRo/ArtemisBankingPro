using System.Globalization;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Commands;

/// <summary>
/// Transfiere fondos desde una cuenta propia activa hacia otra cuenta de ahorro
/// indicada manualmente, registrando el débito y el crédito de forma atómica.
/// </summary>
public sealed record ProcessExpressTransactionCommand(
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
