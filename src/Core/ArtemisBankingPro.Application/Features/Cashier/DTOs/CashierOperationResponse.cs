namespace ArtemisBankingPro.Application.Features.Cashier.DTOs;

/// <summary>
/// Resultado de una operación de cajero procesada (pago a préstamo, pago a
/// tarjeta, etc.). OperationId es el correlation ID de la operación financiera
/// persistida. Solo se completa el identificador del producto pertinente:
/// LoanNumber para pagos a préstamo, CardLastFour para pagos a tarjeta.
/// NotificationWarning es null cuando todas las notificaciones se entregaron;
/// si alguna falló, transporta el mensaje informativo (el dinero ya está
/// confirmado y no se revierte).
/// </summary>
public sealed record CashierOperationResponse(
    Guid OperationId,
    string AccountNumber,
    string? LoanNumber,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string Status,
    string? CardLastFour = null,
    string? NotificationWarning = null
);
