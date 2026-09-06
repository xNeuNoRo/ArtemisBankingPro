using ArtemisBankingPro.Domain.Common.Events;
using ArtemisBankingPro.Domain.Lending.ValueObjects;

namespace ArtemisBankingPro.Domain.Lending.Events;

/// <summary>
/// Se desencadena cuando un préstamo queda saldado (pendiente en cero) tras
/// un pago y pasa a estado Completed. Solo transporta datos seguros.
/// </summary>
public sealed record LoanCompletedEvent(
    string CustomerUserId,
    LoanNumber LoanNumber,
    DateTimeOffset CompletedAt
) : IDomainEvent;
