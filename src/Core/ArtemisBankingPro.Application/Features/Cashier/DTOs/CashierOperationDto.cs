namespace ArtemisBankingPro.Application.Features.Cashier.DTOs;

/// <summary>
/// Operación financiera iniciada por un cajero para el listado paginado.
/// Nunca expone PAN completo, CVC ni tokens.
/// </summary>
public sealed record CashierOperationDto(
    Guid OperationId,
    string Kind,
    string Status,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string? AccountLastFour,
    string? CardLastFour,
    string? LoanNumber,
    string? RejectionCode
);
