using ArtemisBankingPro.Domain.Common.Events;
using ArtemisBankingPro.Domain.Operations.Enums;

namespace ArtemisBankingPro.Domain.Operations.Events;

/// <summary>
/// Se despacha después de confirmar atómicamente la persistencia de una operación
/// financiera aprobada. Su consumidor solo realiza observabilidad post-commit.
/// </summary>
public sealed record FinancialOperationApprovedEvent(Guid OperationId, FinancialOperationKind Kind)
    : IDomainEvent;
