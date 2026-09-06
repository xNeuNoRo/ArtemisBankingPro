namespace ArtemisBankingPro.Application.Features.CreditCard.DTOs;

/// <summary>
/// Resultado interno de una mutación de tarjeta. No modifica los contratos API
/// de actualización, que continúan respondiendo 204 No Content.
/// </summary>
public sealed record CreditCardMutationResponse(string? NotificationWarning = null);
