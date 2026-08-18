using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Features.FinancialProcessors;

/// <summary>
/// Resultado de un procesador financiero: la operación persistida, los montos
/// solicitado/aplicado/cargo y la fecha de ejecución. Los handlers lo usan para
/// responder y notificar sin re-ejecutar reglas financieras.
/// </summary>
public sealed record FinancialOperationOutcome(
    Guid OperationId,
    Money RequestedAmount,
    Money AppliedAmount,
    Money FeeAmount,
    DateTimeOffset OccurredAt);
