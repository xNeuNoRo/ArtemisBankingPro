namespace ArtemisBankingPro.Application.Features.Cashier.DTOs;

/// <summary>
/// Resultado de una operación de cajero procesada (pago a préstamo, etc.).
/// OperationId es el correlation ID de la operación financiera persistida.
/// </summary>
public sealed record CashierOperationResponse(
    Guid OperationId,
    string AccountNumber,
    string LoanNumber,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string Status
);
