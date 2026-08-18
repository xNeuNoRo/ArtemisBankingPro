using System.ComponentModel.DataAnnotations;
using ArtemisBankingPro.Application.Common.ViewModels;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.ViewModels;

public sealed class SavingsAccountListViewModel : BaseViewModel {
    [StringLength(50, ErrorMessage = "El estado no debe exceder 50 caracteres.")]
    public string? Status { get; set; }

    [StringLength(50, ErrorMessage = "El tipo no debe exceder 50 caracteres.")]
    public string? Type { get; set; }

    [StringLength(20, ErrorMessage = "La cédula no debe exceder 20 caracteres.")]
    public string? Identification { get; set; }

    public IReadOnlyList<SelectOptionViewModel> StatusOptions { get; init; } = [];
    public IReadOnlyList<SelectOptionViewModel> TypeOptions { get; init; } = [];
    public IReadOnlyList<SavingsAccountSummaryViewModel> Accounts { get; init; } = [];
    public PaginationViewModel Pagination { get; init; } = new();
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

public sealed class AccountTransactionItemViewModel {
    public int Id { get; init; }
    public DateTimeOffset Date { get; init; }
    public decimal Amount { get; init; }
    public string TransactionType { get; init; } = string.Empty;
    public string Origin { get; init; } = string.Empty;
    public string Beneficiary { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

public sealed class AssignSecondaryAccountViewModel : IValidatableObject {
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
