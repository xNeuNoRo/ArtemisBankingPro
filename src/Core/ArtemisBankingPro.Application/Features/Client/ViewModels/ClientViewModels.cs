using System.ComponentModel.DataAnnotations;
using ArtemisBankingPro.Application.Common.ViewModels;

namespace ArtemisBankingPro.Application.Features.Client.ViewModels;

public sealed class ClientDashboardViewModel : BaseViewModel {
    public MyProductsViewModel Products { get; init; } = new();

    public bool HasNoActiveProducts => Products.IsEmpty;
}

public sealed class MyProductsViewModel {
    public IReadOnlyList<MyAccountViewModel> Accounts { get; init; } = [];
    public IReadOnlyList<MyLoanViewModel> Loans { get; init; } = [];
    public IReadOnlyList<MyCardViewModel> Cards { get; init; } = [];

    public bool IsEmpty => Accounts.Count == 0 && Loans.Count == 0 && Cards.Count == 0;
}

public sealed class MyAccountViewModel {
    public string AccountNumber { get; init; } = string.Empty;
    public decimal Balance { get; init; }
    public string Type { get; init; } = string.Empty;
}

public sealed class MyLoanViewModel {
    public int LoanId { get; init; }
    public string LoanNumber { get; init; } = string.Empty;
    public decimal ApprovedPrincipal { get; init; }
    public int TotalInstallments { get; init; }
    public int PaidInstallments { get; init; }
    public decimal OutstandingAmount { get; init; }
    public decimal AnnualRate { get; init; }
    public int TermMonths { get; init; }
    public bool IsDelinquent { get; init; }
}

public sealed class MyCardViewModel {
    public int CardId { get; init; }
    public string LastFour { get; init; } = string.Empty;
    public decimal CreditLimit { get; init; }
    public decimal AvailableCredit { get; init; }
    public decimal CurrentDebt { get; init; }
    public string Expiration { get; init; } = string.Empty;
}

public sealed class MyAccountTransactionsViewModel : BaseViewModel, IValidatableObject {
    [Required(ErrorMessage = "La cuenta es requerida.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "La cuenta debe contener exactamente 9 dígitos.")]
    public string AccountNumber { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    public DateTimeOffset? DateFrom { get; set; }

    [DataType(DataType.Date)]
    public DateTimeOffset? DateTo { get; set; }

    [StringLength(50, ErrorMessage = "El tipo de transacción no debe exceder 50 caracteres.")]
    public string? TransactionType { get; set; }
    public IReadOnlyList<SelectOptionViewModel> TransactionTypeOptions { get; init; } = [];
    public IReadOnlyList<ClientAccountTransactionItemViewModel> Transactions { get; init; } = [];
    public PaginationViewModel Pagination { get; init; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (DateFrom is not null && DateTo is not null && DateFrom > DateTo) {
            yield return new ValidationResult(
                "La fecha inicial no puede ser posterior a la fecha final.",
                [nameof(DateFrom), nameof(DateTo)]
            );
        }
    }
}

public sealed class ClientAccountTransactionItemViewModel {
    public int Id { get; init; }
    public DateTimeOffset Date { get; init; }
    public decimal Amount { get; init; }
    public string TransactionType { get; init; } = string.Empty;
    public string Origin { get; init; } = string.Empty;
    public string Beneficiary { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

public sealed class MyLoanDetailViewModel : BaseViewModel {
    public int LoanId { get; init; }
    public string LoanNumber { get; init; } = string.Empty;
    public decimal ApprovedPrincipal { get; init; }
    public decimal OutstandingAmount { get; init; }
    public decimal AnnualRate { get; init; }
    public int TermMonths { get; init; }
    public int TotalInstallments { get; init; }
    public int PaidInstallments { get; init; }
    public bool IsDelinquent { get; init; }
    public IReadOnlyList<MyLoanInstallmentViewModel> Amortization { get; init; } = [];
}

public sealed class MyLoanInstallmentViewModel {
    public int InstallmentNumber { get; init; }
    public DateOnly DueDate { get; init; }
    public decimal ScheduledAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsOverdue { get; init; }
}

public sealed class MyCardDetailViewModel : BaseViewModel {
    public int CardId { get; init; }
    public string LastFour { get; init; } = string.Empty;
    public decimal CreditLimit { get; init; }
    public decimal AvailableCredit { get; init; }
    public decimal CurrentDebt { get; init; }
    public string Expiration { get; init; } = string.Empty;
    public IReadOnlyList<MyCardConsumptionViewModel> Consumptions { get; init; } = [];
    public PaginationViewModel Pagination { get; init; } = new();
}

public sealed class MyCardConsumptionViewModel {
    public int Id { get; init; }
    public DateTimeOffset Date { get; init; }
    public decimal Amount { get; init; }
    public string CommerceName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

public sealed class BeneficiaryListViewModel : BaseViewModel {
    public IReadOnlyList<BeneficiaryItemViewModel> Beneficiaries { get; init; } = [];
}

public sealed class BeneficiaryItemViewModel {
    public int BeneficiaryId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;
}

public sealed class AddBeneficiaryViewModel {
    [Required(ErrorMessage = "El número de cuenta es requerido.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "La cuenta debe contener exactamente 9 dígitos.")]
    public string DestinationAccountNumber { get; set; } = string.Empty;
}

public sealed class RemoveBeneficiaryViewModel : ConfirmationViewModel {
    public int BeneficiaryId { get; init; }
    public string BeneficiaryName { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;
}

public sealed class ExpressTransactionViewModel : IValidatableObject {
    [Required(ErrorMessage = "La cuenta de origen es requerida.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "La cuenta debe contener exactamente 9 dígitos.")]
    public string SourceAccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "La cuenta destino es requerida.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "La cuenta debe contener exactamente 9 dígitos.")]
    public string DestinationAccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "El monto a transferir es requerido.")]
    public decimal? Amount { get; set; }

    public IReadOnlyList<SelectOptionViewModel> SourceAccountOptions { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (Amount <= 0m) {
            yield return new ValidationResult(
                "El monto a transferir debe ser mayor que cero.",
                [nameof(Amount)]
            );
        }

        if (!string.IsNullOrWhiteSpace(SourceAccountNumber)
            && !string.IsNullOrWhiteSpace(DestinationAccountNumber)
            && string.Equals(SourceAccountNumber, DestinationAccountNumber, StringComparison.Ordinal)) {
            yield return new ValidationResult(
                "La cuenta origen y la cuenta destino no pueden ser la misma.",
                [nameof(SourceAccountNumber), nameof(DestinationAccountNumber)]
            );
        }
    }
}

public sealed class BeneficiaryTransferViewModel : IValidatableObject {
    [Range(1, int.MaxValue, ErrorMessage = "El beneficiario es requerido.")]
    public int BeneficiaryId { get; set; }

    [Required(ErrorMessage = "La cuenta de origen es requerida.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "La cuenta debe contener exactamente 9 dígitos.")]
    public string SourceAccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "El monto a transferir es requerido.")]
    public decimal? Amount { get; set; }

    public IReadOnlyList<SelectOptionViewModel> BeneficiaryOptions { get; init; } = [];
    public IReadOnlyList<SelectOptionViewModel> SourceAccountOptions { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (Amount <= 0m) {
            yield return new ValidationResult(
                "El monto a transferir debe ser mayor que cero.",
                [nameof(Amount)]
            );
        }
    }
}

public sealed class ClientCardPaymentViewModel : IValidatableObject {
    [Range(1, int.MaxValue, ErrorMessage = "La tarjeta es requerida.")]
    public int CardId { get; set; }

    [Required(ErrorMessage = "La cuenta de origen es requerida.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "La cuenta debe contener exactamente 9 dígitos.")]
    public string AccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "El monto a pagar es requerido.")]
    public decimal? Amount { get; set; }

    public IReadOnlyList<SelectOptionViewModel> CardOptions { get; init; } = [];
    public IReadOnlyList<SelectOptionViewModel> AccountOptions { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (Amount <= 0m) {
            yield return new ValidationResult(
                "El monto a pagar debe ser mayor que cero.",
                [nameof(Amount)]
            );
        }
    }
}

public sealed class ClientLoanPaymentViewModel : IValidatableObject {
    [Range(1, int.MaxValue, ErrorMessage = "El préstamo es requerido.")]
    public int LoanId { get; set; }

    [Required(ErrorMessage = "La cuenta de origen es requerida.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "La cuenta debe contener exactamente 9 dígitos.")]
    public string AccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "El monto a pagar es requerido.")]
    public decimal? Amount { get; set; }

    public IReadOnlyList<SelectOptionViewModel> LoanOptions { get; init; } = [];
    public IReadOnlyList<SelectOptionViewModel> AccountOptions { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (Amount <= 0m) {
            yield return new ValidationResult(
                "El monto a pagar debe ser mayor que cero.",
                [nameof(Amount)]
            );
        }
    }
}

public sealed class CashAdvanceQuoteViewModel : IValidatableObject {
    [Range(1, int.MaxValue, ErrorMessage = "La tarjeta es requerida.")]
    public int CardId { get; set; }

    [Required(ErrorMessage = "El monto del avance es requerido.")]
    public decimal? Amount { get; set; }

    public decimal PrincipalAmount { get; init; }
    public decimal InterestAmount { get; init; }
    public decimal TotalToCharge { get; init; }
    public decimal AvailableCredit { get; init; }
    public bool IsEligible { get; init; }
    public IReadOnlyList<SelectOptionViewModel> CardOptions { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (Amount <= 0m) {
            yield return new ValidationResult(
                "El monto del avance debe ser mayor que cero.",
                [nameof(Amount)]
            );
        }
    }
}

public sealed class CashAdvanceViewModel : IValidatableObject {
    [Range(1, int.MaxValue, ErrorMessage = "La tarjeta es requerida.")]
    public int CardId { get; set; }

    [Required(ErrorMessage = "La cuenta destino es requerida.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "La cuenta debe contener exactamente 9 dígitos.")]
    public string DestinationAccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "El monto del avance es requerido.")]
    public decimal? Amount { get; set; }

    public IReadOnlyList<SelectOptionViewModel> CardOptions { get; init; } = [];
    public IReadOnlyList<SelectOptionViewModel> DestinationAccountOptions { get; init; } = [];
    public CashAdvanceQuoteViewModel? Quote { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (Amount <= 0m) {
            yield return new ValidationResult(
                "El monto del avance debe ser mayor que cero.",
                [nameof(Amount)]
            );
        }
    }
}

public sealed class OwnAccountsTransferViewModel : IValidatableObject {
    [Required(ErrorMessage = "La cuenta de origen es requerida.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "La cuenta debe contener exactamente 9 dígitos.")]
    public string SourceAccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "La cuenta destino es requerida.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "La cuenta debe contener exactamente 9 dígitos.")]
    public string DestinationAccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "El monto a transferir es requerido.")]
    public decimal? Amount { get; set; }

    public IReadOnlyList<SelectOptionViewModel> AccountOptions { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (Amount <= 0m) {
            yield return new ValidationResult(
                "El monto a transferir debe ser mayor que cero.",
                [nameof(Amount)]
            );
        }

        if (!string.IsNullOrWhiteSpace(SourceAccountNumber)
            && !string.IsNullOrWhiteSpace(DestinationAccountNumber)
            && string.Equals(SourceAccountNumber, DestinationAccountNumber, StringComparison.Ordinal)) {
            yield return new ValidationResult(
                "La cuenta origen y la cuenta destino no pueden ser la misma.",
                [nameof(SourceAccountNumber), nameof(DestinationAccountNumber)]
            );
        }
    }
}
