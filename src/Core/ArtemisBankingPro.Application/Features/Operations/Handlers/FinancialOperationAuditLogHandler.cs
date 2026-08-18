using ArtemisBankingPro.Application.Interfaces.Events;
using ArtemisBankingPro.Domain.Operations.Events;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Operations.Handlers;

/// <summary>
/// Consumidor de <see cref="FinancialOperationApprovedEvent"/> (ADR-012):
/// registra en Serilog, después del commit, la aprobación de una operación
/// financiera para trazabilidad de auditoría.
/// </summary>
/// <remarks>
/// El despacho ocurre post-commit en <c>BankingDbContext</c> (nunca dentro de
/// la transacción), por lo que este handler nunca participa en la atomicidad
/// financiera. Solo transporta datos seguros: <c>OperationId</c> y
/// <c>Kind</c>; nunca montos, identificadores personales ni datos de tarjeta.
/// </remarks>
public sealed class FinancialOperationAuditLogHandler
    : IEventHandler<FinancialOperationApprovedEvent> {
    private readonly ILogger<FinancialOperationAuditLogHandler> _logger;

    public FinancialOperationAuditLogHandler(
        ILogger<FinancialOperationAuditLogHandler> logger
    ) {
        _logger = logger;
    }

    public Task HandleAsync(
        FinancialOperationApprovedEvent domainEvent,
        CancellationToken ct = default
    ) {
        _logger.LogInformation(
            "Operación financiera aprobada: {OperationId} de tipo {OperationKind}.",
            domainEvent.OperationId,
            domainEvent.Kind
        );

        return Task.CompletedTask;
    }
}
