using ArtemisBankingPro.Application.Features.Operations.Handlers;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Domain.Operations.Events;
using Microsoft.Extensions.Logging;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Operations.Handlers;

/// <summary>
/// Consumidor de auditoría de <see cref="FinancialOperationApprovedEvent"/>
/// (ADR-012): el despacho ocurre post-commit y el log solo transporta datos
/// seguros (OperationId y Kind), nunca montos ni datos personales.
/// </summary>
public sealed class FinancialOperationAuditLogHandlerTests {
    [Fact]
    public async Task HandleAsync_ApprovedOperation_LogsCorrelationWithoutSensitiveData() {
        var logger = new Mock<ILogger<FinancialOperationAuditLogHandler>>();
        var handler = new FinancialOperationAuditLogHandler(logger.Object);
        Guid operationId = Guid.NewGuid();

        await handler.HandleAsync(
            new FinancialOperationApprovedEvent(operationId, FinancialOperationKind.Deposit)
        );

        logger.Verify(
            log => log.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("Operación financiera aprobada")
                    && state.ToString()!.Contains(operationId.ToString())
                    && state.ToString()!.Contains(nameof(FinancialOperationKind.Deposit))
                    && !state.ToString()!.Contains("CVC")
                    && !state.ToString()!.Contains("Pan")
                ),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task HandleAsync_IsSafeAfterCommit_CompletesWithoutThrowing() {
        var handler = new FinancialOperationAuditLogHandler(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                FinancialOperationAuditLogHandler
            >.Instance
        );

        var act = async () => await handler.HandleAsync(
            new FinancialOperationApprovedEvent(Guid.NewGuid(), FinancialOperationKind.Withdrawal)
        );

        await act.Should().NotThrowAsync();
    }
}
