using ArtemisBankingPro.Application.Features.Overdue.Commands;
using ArtemisBankingPro.Application.Features.Overdue.DTOs;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Functions;

/// <summary>
/// Daily overdue processing adapter. Business rules remain in Application;
/// repeated invocations are safe because the command is idempotent.
/// </summary>
public sealed class ProcessOverdueLoansFunction(
    IMediator mediator,
    IBusinessClock businessClock,
    ILogger<ProcessOverdueLoansFunction> logger
) {
    [Function(nameof(ProcessOverdueLoansFunction))]
    [FixedDelayRetry(3, "00:05:00")]
    public async Task Run(
        [TimerTrigger("%TimeTrigger%")] TimerInfo timerInfo,
        CancellationToken cancellationToken
    ) {
        if (timerInfo.IsPastDue) {
            logger.LogWarning("El trigger de mora se ejecutó fuera de horario.");
        }

        Result<OverdueProcessingResult> result = await mediator.Send(
            new ProcessOverdueLoansCommand(businessClock.Today),
            cancellationToken
        );

        if (result.IsFailure) {
            throw new InvalidOperationException(
                $"No se pudo procesar la mora: {result.Error?.Code ?? "Unknown"}."
            );
        }

        if (result.Value.FailedCount > 0) {
            throw new InvalidOperationException(
                $"El procesamiento de mora terminó con {result.Value.FailedCount} fallos."
            );
        }

        logger.LogInformation(
            "Mora procesada para {BusinessDate}: {TotalProcessed} préstamos, {NewDelinquent} nuevos morosos.",
            businessClock.Today,
            result.Value.TotalProcessed,
            result.Value.NewDelinquent
        );
    }
}
