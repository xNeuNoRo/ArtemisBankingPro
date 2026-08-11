using ArtemisBankingPro.Domain.Operations.Enums;

namespace ArtemisBankingPro.Application.Features.Cashier.DTOs;

/// <summary>
/// Filtros tipados del listado de operaciones del cajero.
/// Las fechas son fechas de negocio (zona America/Santo_Domingo).
/// </summary>
public sealed record CashierOperationFilters(
    FinancialOperationKind? Kind = null,
    FinancialOperationStatus? Status = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null
);
