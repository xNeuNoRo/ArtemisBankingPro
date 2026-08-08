using ArtemisBankingPro.Domain.Common.Events;
using ArtemisBankingPro.Domain.Operations.Enums;

namespace ArtemisBankingPro.Domain.Operations.Events;

/// <summary>
/// Se desencadena cuando una operación financiera se aprueba (se persiste y confirma
/// atómicamente). El handler de notificaciones relée la operación por su Id
/// para componer el correo correspondiente.
/// </summary>
public sealed record FinancialOperationApprovedEvent(Guid OperationId, FinancialOperationKind Kind)
    : IDomainEvent;
