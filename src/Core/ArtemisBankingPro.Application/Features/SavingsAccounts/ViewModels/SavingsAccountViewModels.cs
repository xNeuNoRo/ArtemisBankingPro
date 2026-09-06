using System.ComponentModel.DataAnnotations;
using ArtemisBankingPro.Application.Common.Validation;
using ArtemisBankingPro.Application.Features.Admin.ViewModels;
using ArtemisBankingPro.Application.Common.ViewModels;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.ViewModels;

public sealed class SavingsAccountListViewModel : BaseViewModel {
    public string? LoadErrorMessage { get; init; }
    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadErrorMessage);

    [StringLength(50, ErrorMessage = "El estado no debe exceder 50 caracteres.")]
    public string? Status { get; set; }

    [StringLength(50, ErrorMessage = "El tipo no debe exceder 50 caracteres.")]
    public string? Type { get; set; }

    [StringLength(
        IdentityValidationLimits.IdentificationMaxLength,
        ErrorMessage = IdentityValidationLimits.IdentificationMaxLengthMessage
    )]
    public string? Identification { get; set; }

    public IReadOnlyList<SelectOptionViewModel> StatusOptions { get; init; } = [];
    public IReadOnlyList<SelectOptionViewModel> TypeOptions { get; init; } = [];
    public IReadOnlyList<SavingsAccountSummaryViewModel> Accounts { get; init; } = [];
    public PaginationViewModel Pagination { get; init; } = new();

    public static IReadOnlyList<SelectOptionViewModel> BuildStatusOptions(
        string? selectedStatus,
        bool searchingByIdentification = false
    ) => [
        new() {
            Value = "activa",
            Text = "Activas",
            IsSelected = (!searchingByIdentification && string.IsNullOrWhiteSpace(selectedStatus))
                || string.Equals(selectedStatus, "activa", StringComparison.OrdinalIgnoreCase),
        },
        new() {
            Value = "cancelada",
            Text = "Canceladas",
            IsSelected = string.Equals(selectedStatus, "cancelada", StringComparison.OrdinalIgnoreCase),
        },
        new() {
            Value = "todas",
            Text = "Todas",
            IsSelected = (searchingByIdentification && string.IsNullOrWhiteSpace(selectedStatus))
                || string.Equals(selectedStatus, "todas", StringComparison.OrdinalIgnoreCase),
        },
    ];

    public static IReadOnlyList<SelectOptionViewModel> BuildTypeOptions(string? selectedType) => [
        new() {
            Value = string.Empty,
            Text = "Todas",
            IsSelected = string.IsNullOrWhiteSpace(selectedType),
        },
        new() {
            Value = "principal",
            Text = "Principal",
            IsSelected = string.Equals(selectedType, "principal", StringComparison.OrdinalIgnoreCase),
        },
        new() {
            Value = "secundaria",
            Text = "Secundaria",
            IsSelected = string.Equals(selectedType, "secundaria", StringComparison.OrdinalIgnoreCase),
        },
    ];
}

public sealed class SavingsAccountSummaryViewModel {
    public int Id { get; init; }
    public string AccountNumber { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string ClientFullName { get; init; } = string.Empty;
    public string Identification { get; init; } = string.Empty;
    public decimal Balance { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class AccountDetailViewModel : BaseViewModel {
    public string AccountNumber { get; init; } = string.Empty;
    public string ClientFullName { get; init; } = string.Empty;
    public decimal Balance { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public IReadOnlyList<AccountTransactionItemViewModel> Transactions { get; init; } = [];
    public PaginationViewModel Pagination { get; init; } = new();
}

public sealed class SavingsAccountAssignmentPageViewModel : BaseViewModel {
    public EligibleClientItemViewModel Customer { get; init; } = new();
    public AssignSecondaryAccountViewModel Form { get; init; } = new();
    public string SubmissionToken { get; init; } = string.Empty;
}

public sealed class AccountTransactionItemViewModel {
    public int Id { get; init; }
    public DateTimeOffset Date { get; init; }
    public decimal Amount { get; init; }
    public string TransactionType { get; init; } = string.Empty;
    public string Origin { get; init; } = string.Empty;
    public string Beneficiary { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

public sealed class AssignSecondaryAccountViewModel : BaseViewModel, IValidatableObject {
    [Required(ErrorMessage = "El balance inicial es requerido.")]
    public decimal? InitialAmount { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (InitialAmount < 0m) {
            yield return new ValidationResult(
                "El balance inicial no puede ser negativo.",
                [nameof(InitialAmount)]
            );
        }
    }
}

public sealed class CancelSecondaryAccountViewModel : ConfirmationViewModel {
    public string AccountNumber { get; init; } = string.Empty;
}
