using System.Globalization;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.HermesPay.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.HermesPay.Commands;

/// <summary>
/// Procesa un pago con tarjeta de crédito a favor de un comercio (spec §41,
/// POST /pay/process-payment/{commerceId}). Con rol Comercio el comercio se
/// deriva del JWT y el <c>CommerceId</c> recibido se ignora; con rol
/// Administrador se usa el <c>CommerceId</c> del command.
/// </summary>
/// <remarks>
/// La validación de rol se realiza en el handler (no implementa
/// <see cref="IAuthorize"/>): el rol Comercio usa el commerce_id del JWT y el
/// rol Administrador el commerceId del request.
/// </remarks>
public sealed record ProcessHermesPayCommand(
    int? CommerceId,
    string CardNumber,
    string MonthExpirationCard,
    string YearExpirationCard,
    string Cvc,
    decimal TransactionAmount,
    string IdempotencyKey
) : IRequest<Result<ProcessHermesPayResponse>>, IIdempotentCommand {
    /// <summary>
    /// Huella canónica no sensible de la operación. La clave de idempotencia la
    /// suministra el caller y no deriva del payload ni del reloj.
    /// PAN y CVC se excluyen deliberadamente. La clave de idempotencia es un
    /// identificador opaco del intento, no una huella de datos de autenticación.
    /// </summary>
    public string RequestFingerprint =>
        $"{CommerceId?.ToString(CultureInfo.InvariantCulture) ?? "none"}|{MonthExpirationCard}/{YearExpirationCard}|{TransactionAmount.ToString("0.00", CultureInfo.InvariantCulture)}";

}
