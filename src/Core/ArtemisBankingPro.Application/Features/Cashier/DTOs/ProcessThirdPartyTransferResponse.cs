namespace ArtemisBankingPro.Application.Features.Cashier.DTOs;

/// <summary>
/// Resultado de una transferencia a cuentas de terceros procesada por cajero.
/// OperationId es el correlation ID compartido por el débito y el crédito.
/// </summary>
public sealed record ProcessThirdPartyTransferResponse(
    Guid OperationId,
    string SourceAccountNumber,
    string DestinationAccountNumber,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string Status
);
