using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
    decimal TransactionAmount
) : IRequest<Result<ProcessHermesPayResponse>>, IIdempotentCommand {
    /// <summary>
    /// Clave de idempotencia por operación, actor y minuto:
    /// hermes-pay-{commerceId}-{huellaPan}-{monto}-{yyyyMMddHHmm}. La huella
    /// del PAN es SHA-256 no cifrado porque la clave debe poder calcularse en
    /// la capa de Application (sin acceso a la clave HMAC de infraestructura);
    /// nunca se persiste el PAN ni el CVC en texto plano.
    /// </summary>
    public string IdempotencyKey =>
        $"hermes-pay-{CommerceId}-{ComputeSha256Hex(CardNumber)}-{TransactionAmount.ToString("0.00", CultureInfo.InvariantCulture)}-{TimeProvider.System.GetUtcNow():yyyyMMddHHmm}";

    /// <summary>
    /// Huella SHA-256 del payload canónico: el payload contiene PAN y CVC, por
    /// lo que nunca se persiste en texto plano (columna de 64 caracteres).
    /// </summary>
    public string RequestFingerprint =>
        ComputeSha256Hex(
            $"{CardNumber}|{MonthExpirationCard}/{YearExpirationCard}|{Cvc}|{TransactionAmount.ToString("0.00", CultureInfo.InvariantCulture)}"
        );

    private static string ComputeSha256Hex(string value) {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
