using System.Globalization;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Commands;

/// <summary>
/// Realiza un avance de efectivo desde una tarjeta propia hacia una cuenta propia.
/// La cuenta recibe el principal y la tarjeta carga el principal más 6.25 % de interés.
/// </summary>
public sealed record ProcessCashAdvanceCommand(
    int CardId,
    string DestinationAccountNumber,
    decimal Amount
) : IRequest<Result<Unit>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Cliente"];

    public string IdempotencyKey =>
        $"cash-advance-{CardId}-{DestinationAccountNumber}-{FormatAmount()}-{TimeProvider.System.GetUtcNow():yyyyMMddHHmm}";

    public string RequestFingerprint =>
        $"{CardId}|{DestinationAccountNumber}|{FormatAmount()}";

    private string FormatAmount() => Amount.ToString("0.00", CultureInfo.InvariantCulture);
}
