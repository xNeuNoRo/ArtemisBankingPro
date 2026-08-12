namespace ArtemisBankingPro.Application.Features.HermesPay.DTOs;

/// <summary>
/// Transacción de un comercio recibida vía Hermes Pay (spec §41,
/// GET /pay/get-transactions). Campos exactos del arreglo <c>data</c>:
/// id, transactionDate, amount, cardLastFourDigits y status (APROBADO /
/// RECHAZADO). Nunca transporta el número completo de la tarjeta.
/// </summary>
public sealed record CommerceTransactionDto(
    string Id,
    DateTimeOffset TransactionDate,
    decimal Amount,
    string CardLastFourDigits,
    string Status
);
