using System.Globalization;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Commands;

/// <summary>
/// Paga una tarjeta propia con fondos de una cuenta propia. El monto efectivo se
/// limita a la deuda real para impedir sobrepagos.
/// </summary>
public sealed record ProcessClientCardPaymentCommand(
    int CardId,
    string AccountNumber,
    decimal Amount
) : IRequest<Result<Unit>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Cliente"];

    public string IdempotencyKey =>
        $"client-card-payment-{CardId}-{AccountNumber}-{FormatAmount()}-{TimeProvider.System.GetUtcNow():yyyyMMddHHmm}";

    public string RequestFingerprint => $"{CardId}|{AccountNumber}|{FormatAmount()}";

    private string FormatAmount() => Amount.ToString("0.00", CultureInfo.InvariantCulture);
}
