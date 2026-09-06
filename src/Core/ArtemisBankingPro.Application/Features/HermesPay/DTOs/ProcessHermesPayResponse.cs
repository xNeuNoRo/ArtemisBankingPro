namespace ArtemisBankingPro.Application.Features.HermesPay.DTOs;

/// <summary>
/// Resultado del procesamiento de un pago Hermes Pay (spec §41). El éxito
/// responde 204 No Content sin cuerpo; el DTO transporta la correlación de la
/// operación aprobada para trazabilidad interna. NotificationWarning es null
/// cuando todas las notificaciones se entregaron; si alguna falló, transporta
/// el mensaje informativo (el cobro ya está confirmado y no se revierte).
/// </summary>
public sealed record ProcessHermesPayResponse(
    Guid OperationId,
    string Status,
    string? NotificationWarning = null
);
