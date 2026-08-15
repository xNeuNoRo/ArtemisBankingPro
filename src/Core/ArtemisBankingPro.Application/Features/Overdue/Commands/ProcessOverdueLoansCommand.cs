using System.Globalization;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Overdue.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Overdue.Commands;

/// <summary>
/// Recalcula la mora de los préstamos activos para una fecha de negocio,
/// procesándolos en lotes independientes e idempotentes por fecha.
/// </summary>
public sealed record ProcessOverdueLoansCommand(DateOnly BusinessDate, int BatchSize = 100)
    : IRequest<Result<OverdueProcessingResult>>, IIdempotentCommand {
    public string IdempotencyKey =>
        $"overdue-loans-{BusinessDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";

    public string RequestFingerprint =>
        $"{BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|{BatchSize.ToString(CultureInfo.InvariantCulture)}";

    public string? IdempotencyActorId => "system:overdue-loans";
}
