using ArtemisBankingPro.Application.Features.Overdue.Commands;
using ArtemisBankingPro.Application.Features.Overdue.DTOs;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;
using Microsoft.ApplicationInsights;
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
    ILogger<ProcessOverdueLoansFunction> logger,
    TelemetryClient telemetryClient
) {
    [Function(nameof(ProcessOverdueLoansFunction))]
    [FixedDelayRetry(3, "00:05:00")]
    public async Task Run(
        [TimerTrigger("%TimeTrigger%")] TimerInfo timerInfo,
        FunctionContext functionContext,
        CancellationToken cancellationToken
    ) {
        DateOnly businessDate = businessClock.Today;
        string invocationId = functionContext.InvocationId;
        using IDisposable? logScope = logger.BeginScope(
            new Dictionary<string, object?> {
                ["InvocationId"] = invocationId,
                ["BusinessDate"] = businessDate,
            }
        );

        if (timerInfo.IsPastDue) {
            logger.LogWarning(
                "El trigger de mora se ejecutó fuera de horario ({InvocationId}).",
                invocationId
            );
        }

        Result<OverdueProcessingResult> result = await mediator.Send(
            new ProcessOverdueLoansCommand(businessDate),
            cancellationToken
        );

        if (result.IsFailure) {
            if (telemetryClient.IsEnabled()) {
                telemetryClient.TrackEvent(
                    "Artemis.OverdueProcessingFailed",
                    new Dictionary<string, string> {
                        ["InvocationId"] = invocationId,
                        ["BusinessDate"] = businessDate.ToString("yyyy-MM-dd"),
                        ["ErrorCode"] = result.Error?.Code ?? "Unknown",
                    }
                );
            }
            throw new InvalidOperationException(
                $"No se pudo procesar la mora: {result.Error?.Code ?? "Unknown"}."
            );
        }

        if (result.Value.FailedCount > 0) {
            logger.LogError(
                "El procesamiento de mora terminó con {FailedCount} fallos de procesamiento.",
                result.Value.FailedCount
            );
        }

        if (result.Value.EmailFailedCount > 0) {
            logger.LogError(
                "El procesamiento de mora terminó con {EmailFailedCount} fallos de correo.",
                result.Value.EmailFailedCount
            );
        }

        if (result.Value.HasMore) {
            logger.LogWarning(
                "El procesamiento de mora alcanzó el límite de préstamos por invocación; se requiere continuar."
            );
        }

        if (telemetryClient.IsEnabled()) {
            telemetryClient.TrackEvent(
                "Artemis.OverdueProcessingCompleted",
                new Dictionary<string, string> {
                    ["InvocationId"] = invocationId,
                    ["BusinessDate"] = businessDate.ToString("yyyy-MM-dd"),
                    ["PastDue"] = timerInfo.IsPastDue.ToString(),
                    ["HasMore"] = result.Value.HasMore.ToString(),
                }
            );
            telemetryClient.TrackMetric("Artemis.Overdue.TotalProcessed", result.Value.TotalProcessed);
            telemetryClient.TrackMetric("Artemis.Overdue.NewDelinquent", result.Value.NewDelinquent);
            telemetryClient.TrackMetric("Artemis.Overdue.ProcessingFailures", result.Value.FailedCount);
            telemetryClient.TrackMetric("Artemis.Overdue.EmailFailures", result.Value.EmailFailedCount);
            telemetryClient.TrackMetric("Artemis.Overdue.HasMore", result.Value.HasMore ? 1 : 0);
        }

        if (result.Value.FailedCount > 0 || result.Value.HasMore) {
            throw new InvalidOperationException(
                $"El procesamiento de mora requiere otra ejecución: {result.Value.FailedCount} fallos, HasMore={result.Value.HasMore}."
            );
        }

        logger.LogInformation(
            "Mora procesada para {BusinessDate}: {TotalProcessed} préstamos, {NewDelinquent} nuevos morosos, {FailedCount} fallos de procesamiento, {EmailFailedCount} fallos de correo.",
            businessDate,
            result.Value.TotalProcessed,
            result.Value.NewDelinquent,
            result.Value.FailedCount,
            result.Value.EmailFailedCount
        );
    }
}
