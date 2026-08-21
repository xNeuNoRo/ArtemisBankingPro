using System.Globalization;
using ArtemisBankingPro.Application.Features.Overdue.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Overdue.Commands;

/// <summary>
/// Recalcula la mora de los préstamos activos para una fecha de negocio,
/// procesándolos en lotes acotados.
/// </summary>
/// <remarks>
/// El procesamiento es naturalmente idempotente: <c>RefreshDelinquency</c>
/// recalcula el mismo estado al repetirse, no crea operaciones financieras y el
/// rowversion del préstamo arbitra escrituras concurrentes. Por eso este
/// command NO implementa
/// <see cref="ArtemisBankingPro.Application.Common.Interfaces.IIdempotentCommand"/>
/// (ADR-010): un crash
/// no bloquea el día con una reserva <c>InProgress</c> ni marca el día como
/// procesado cuando quedaron préstamos sin actualizar; el reintento del mismo
/// día es seguro y el resultado informa <c>FailedCount</c> y
/// <c>EmailFailedCount</c> para que el host observe fallos sin convertir un
/// cambio de mora confirmado en una operación financiera fallida. El handler
/// limita cada ejecución a 1,000 préstamos y devuelve <c>HasMore</c> cuando
/// debe continuar.
/// </remarks>
public sealed record ProcessOverdueLoansCommand(DateOnly BusinessDate, int BatchSize = 100)
    : IRequest<Result<OverdueProcessingResult>> {
    public string RequestFingerprint =>
        $"{BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|{BatchSize.ToString(CultureInfo.InvariantCulture)}";
}
