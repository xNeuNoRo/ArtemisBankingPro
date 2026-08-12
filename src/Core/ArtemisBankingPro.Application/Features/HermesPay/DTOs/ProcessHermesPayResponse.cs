namespace ArtemisBankingPro.Application.Features.HermesPay.DTOs;

/// <summary>
/// Resultado del procesamiento de un pago Hermes Pay (spec §41). El éxito
/// responde 204 No Content sin cuerpo; el DTO transporta la correlación de la
/// operación aprobada para trazabilidad interna.
/// </summary>
public sealed record ProcessHermesPayResponse(Guid OperationId, string Status);
