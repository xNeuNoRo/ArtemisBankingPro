using System.ComponentModel.DataAnnotations;
using ArtemisBankingPro.Application.Features.Admin.ViewModels;
using ArtemisBankingPro.Application.Common.ViewModels;

namespace ArtemisBankingPro.Application.Features.Loans.ViewModels;

public sealed class LoanListViewModel : BaseViewModel {
    public string? LoadErrorMessage { get; init; }
    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadErrorMessage);

    [StringLength(50, ErrorMessage = "El estado no debe exceder 50 caracteres.")]
    public string? Status { get; set; }

    [StringLength(20, ErrorMessage = "La cédula no debe exceder 20 caracteres.")]
    public string? Identification { get; set; }

    public IReadOnlyList<SelectOptionViewModel> StatusOptions { get; init; } = [];
    public IReadOnlyList<LoanListItemViewModel> Loans { get; init; } = [];
    public PaginationViewModel Pagination { get; init; } = new();

    public static IReadOnlyList<SelectOptionViewModel> BuildStatusOptions(
        string? selectedStatus,
        bool searchingByIdentification = false
    ) => [
        new() {
            Value = "activos",
            Text = "Activos",
            IsSelected = (!searchingByIdentification && string.IsNullOrWhiteSpace(selectedStatus))
                || string.Equals(selectedStatus, "activos", StringComparison.OrdinalIgnoreCase),
        },
        new() {
            Value = "completados",
            Text = "Completados",
            IsSelected = string.Equals(selectedStatus, "completados", StringComparison.OrdinalIgnoreCase),
        },
        new() {
            Value = "todos",
            Text = "Todos",
            IsSelected = (searchingByIdentification && string.IsNullOrWhiteSpace(selectedStatus))
                || string.Equals(selectedStatus, "todos", StringComparison.OrdinalIgnoreCase),
        },
    ];
}

public sealed class LoanListItemViewModel {
    public int LoanId { get; init; }
    public string LoanNumber { get; init; } = string.Empty;
    public string CustomerUserId { get; init; } = string.Empty;
    public string CustomerFullName { get; init; } = string.Empty;
    public decimal CapitalAmount { get; init; }
    public int TotalInstallments { get; init; }
    public int PaidInstallments { get; init; }
    public decimal PendingAmount { get; init; }
    public decimal AnnualInterestRate { get; init; }
    public int TermMonths { get; init; }
    public string Status { get; init; } = string.Empty;
    public string CustomerPaymentStatus { get; init; } = string.Empty;
    public DateTimeOffset IssuedAt { get; init; }
}

public sealed class LoanDetailViewModel : BaseViewModel {
    public int LoanId { get; init; }
    public string LoanNumber { get; init; } = string.Empty;
    public string CustomerUserId { get; init; } = string.Empty;
    public string CustomerFullName { get; init; } = string.Empty;
    public decimal CapitalAmount { get; init; }
    public decimal AnnualInterestRate { get; init; }
    public int TermMonths { get; init; }
    public decimal MonthlyInstallment { get; init; }
    public decimal PendingAmount { get; init; }
    public string Status { get; init; } = string.Empty;
    public string CustomerPaymentStatus { get; init; } = string.Empty;
    public DateTimeOffset IssuedAt { get; init; }
    public IReadOnlyList<LoanInstallmentViewModel> Amortization { get; init; } = [];
}

public sealed class LoanAssignmentPageViewModel : BaseViewModel {
    public EligibleClientItemViewModel Customer { get; init; } = new();
    public CreateLoanViewModel Form { get; init; } = new();
    public string SubmissionToken { get; init; } = string.Empty;
}

public sealed class LoanRatePageViewModel : BaseViewModel {
    public LoanDetailViewModel Loan { get; init; } = new();
    public UpdateLoanRateViewModel Form { get; init; } = new();
    public string SubmissionToken { get; init; } = string.Empty;
}

public sealed class LoanInstallmentViewModel {
    public int InstallmentNumber { get; init; }
    public DateOnly DueDate { get; init; }
    public decimal InstallmentAmount { get; init; }
    public decimal InterestAmount { get; init; }
    public decimal CapitalAmount { get; init; }
    public decimal PendingInstallmentAmount { get; init; }
    public string PaymentStatus { get; init; } = string.Empty;
    public bool IsLate { get; init; }
}

public sealed class CreateLoanViewModel : BaseViewModel, IValidatableObject {
    [Required(ErrorMessage = "El monto a prestar es requerido.")]
    public decimal? CapitalAmount { get; set; }

    [Required(ErrorMessage = "El plazo del préstamo es requerido.")]
    public int? TermMonths { get; set; }

    [Required(ErrorMessage = "La tasa de interés anual es requerida.")]
    public decimal? AnnualInterestRate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (CapitalAmount <= 0m) {
            yield return new ValidationResult(
                "El monto a prestar debe ser mayor que cero.",
                [nameof(CapitalAmount)]
            );
        }

        if (TermMonths is not (6 or 12 or 18 or 24 or 30 or 36 or 42 or 48 or 54 or 60)) {
            yield return new ValidationResult(
                "El plazo seleccionado no es válido.",
                [nameof(TermMonths)]
            );
        }

        if (AnnualInterestRate < 0m) {
            yield return new ValidationResult(
                "La tasa de interés anual no puede ser negativa.",
                [nameof(AnnualInterestRate)]
            );
        }
    }
}

public sealed class UpdateLoanRateViewModel : BaseViewModel, IValidatableObject {
    [Required(ErrorMessage = "La tasa de interés anual es requerida.")]
    public decimal? AnnualInterestRate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (AnnualInterestRate < 0m) {
            yield return new ValidationResult(
                "La tasa de interés anual no puede ser negativa.",
                [nameof(AnnualInterestRate)]
            );
        }
    }
}

public sealed class HighRiskLoanConfirmationViewModel : ConfirmationViewModel {
    public string CustomerUserId { get; init; } = string.Empty;
    public string CustomerIdentification { get; init; } = string.Empty;
    public string CustomerFullName { get; init; } = string.Empty;
    public decimal CapitalAmount { get; init; }
    public int TermMonths { get; init; }
    public decimal AnnualInterestRate { get; init; }
    public decimal CurrentDebt { get; init; }
    public decimal ProjectedDebt { get; init; }
    public decimal AverageDebt { get; init; }
}
