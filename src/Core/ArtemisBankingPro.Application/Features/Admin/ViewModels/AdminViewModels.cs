using System.ComponentModel.DataAnnotations;
using ArtemisBankingPro.Application.Common.ViewModels;

namespace ArtemisBankingPro.Application.Features.Admin.ViewModels;

/// <summary>Indicadores administrativos calculados por Application.</summary>
public sealed class AdminDashboardViewModel : BaseViewModel {
    public string? LoadErrorMessage { get; init; }

    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadErrorMessage);

    public int TotalTransactionsHistorical { get; init; }
    public int TransactionsToday { get; init; }
    public int TotalPaymentsHistorical { get; init; }
    public int PaymentsToday { get; init; }
    public int ActiveClients { get; init; }
    public int InactiveClients { get; init; }
    public int TotalFinancialProducts { get; init; }
    public int ActiveLoans { get; init; }
    public int ActiveCreditCards { get; init; }
    public int ActiveSavingsAccounts { get; init; }
    public decimal AverageDebtPerClient { get; init; }
}

/// <summary>Cliente elegible para una asignación administrativa.</summary>
public sealed class EligibleClientItemViewModel {
    public string ClientId { get; init; } = string.Empty;
    public string Identification { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public decimal TotalDebt { get; init; }
}

/// <summary>Listado paginado de clientes elegibles.</summary>
public sealed class EligibleClientsViewModel : BaseViewModel {
    [StringLength(20, ErrorMessage = "La cédula no debe exceder 20 caracteres.")]
    public string? Identification { get; set; }

    /// <summary>
    /// Selección de formulario; el caso de uso debe revalidar existencia,
    /// estado y elegibilidad antes de usarla.
    /// </summary>
    [StringLength(450, ErrorMessage = "El identificador del cliente no es válido.")]
    public string? SelectedClientId { get; set; }

    public decimal AverageDebt { get; init; }
    public IReadOnlyList<EligibleClientItemViewModel> Clients { get; init; } = [];
    public PaginationViewModel Pagination { get; init; } = new();
}
