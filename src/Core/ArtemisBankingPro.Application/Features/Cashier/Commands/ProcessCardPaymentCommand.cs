using System.Globalization;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Cashier.Commands;

/// <summary>
/// Pago a tarjeta de crédito realizado por un cajero (spec §29): debita la
/// cuenta de ahorro indicada y aplica el pago a la deuda de la tarjeta,
/// capando el monto efectivo a la deuda real. Atómico: débito de cuenta,
/// reducción de deuda, operación financiera y evento de dominio.
/// </summary>
public sealed record ProcessCardPaymentCommand(
    int CardId,
    string AccountNumber,
    decimal Amount,
    string IdempotencyKey
) : IRequest<Result<CashierOperationResponse>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Cajero"];

    public string RequestFingerprint =>
        $"{CardId}|{AccountNumber}|{Amount.ToString("0.00", CultureInfo.InvariantCulture)}";
}
