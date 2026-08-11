using ArtemisBankingPro.Domain.Operations.Enums;

namespace ArtemisBankingPro.Application.Features.Cashier.DTOs;

/// <summary>
/// Filtros tipados del listado de operaciones del cajero. Los instantes de
/// fecha son inclusivos en ambos extremos.
/// </summary>
public sealed record CashierOperationFilters(
    DateTimeOffset? DateFrom = null,
    DateTimeOffset? DateTo = null,
    FinancialOperationKind? Kind = null
);
