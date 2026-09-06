namespace ArtemisBankingPro.Application.Features.HermesPay.DTOs;

/// <summary>
/// Respuesta del procesamiento de un pago Hermes Pay (spec §41,
/// POST /pay/process-payment). El éxito responde 204 sin cuerpo; este DTO
/// transporta el mensaje del rechazo (400), por ejemplo:
/// <c>{"message": "El monto de la transacción excede el crédito disponible de
/// la tarjeta."}</c>
/// </summary>
public sealed record ProcessHermesPayResponseDto(string Message);
