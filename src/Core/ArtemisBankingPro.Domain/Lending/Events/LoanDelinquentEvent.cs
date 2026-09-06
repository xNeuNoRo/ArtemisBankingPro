using ArtemisBankingPro.Domain.Common.Events;
using ArtemisBankingPro.Domain.Lending.ValueObjects;

namespace ArtemisBankingPro.Domain.Lending.Events;

/// <summary>
/// Se desencadena cuando un préstamo pasa de estar al día a tener al menos una
/// cuota vencida pendiente. Solo transporta datos seguros.
/// </summary>
public sealed record LoanDelinquentEvent(
    string CustomerUserId,
    LoanNumber LoanNumber,
    DateOnly BusinessDate
) : IDomainEvent;
