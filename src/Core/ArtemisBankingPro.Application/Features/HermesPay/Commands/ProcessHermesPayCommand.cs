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
    decimal TransactionAmount,
    string IdempotencyKey
) : IRequest<Result<ProcessHermesPayResponse>>, IIdempotentCommand {
    /// <summary>
    /// Huella SHA-256 del payload canónico: el payload contiene PAN y CVC, por
    /// lo que nunca se persiste en texto plano (columna de 64 caracteres).
    /// La clave de idempotencia la suministra el caller (header Idempotency-Key);
    /// no deriva del PAN ni del reloj.
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
