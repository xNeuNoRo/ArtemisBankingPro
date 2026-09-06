namespace ArtemisBankingPro.Application.Features.Loans.DTOs;

/// <summary>
/// Resultado interno de la actualización de tasa. La API mantiene 204 No Content;
/// MVC usa la advertencia para informar un fallo de notificación posterior al commit.
/// </summary>
public sealed record LoanRateUpdateResponse(string? NotificationWarning = null);
