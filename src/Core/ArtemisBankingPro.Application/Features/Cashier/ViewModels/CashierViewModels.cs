using System.ComponentModel.DataAnnotations;
using ArtemisBankingPro.Application.Common.ViewModels;

namespace ArtemisBankingPro.Application.Features.Cashier.ViewModels;

public sealed class CashierDashboardViewModel : BaseViewModel {
    public int TransactionsToday { get; init; }
    public int PaymentsToday { get; init; }
    public int DepositsToday { get; init; }
    public int WithdrawalsToday { get; init; }
}

public sealed class CashierOperationListViewModel : BaseViewModel, IValidatableObject {
    [DataType(DataType.Date)]
    public DateTimeOffset? DateFrom { get; set; }

    [DataType(DataType.Date)]
    public DateTimeOffset? DateTo { get; set; }

    [StringLength(50, ErrorMessage = "El tipo de operación no debe exceder 50 caracteres.")]
    public string? OperationType { get; set; }
    public IReadOnlyList<SelectOptionViewModel> OperationTypeOptions { get; init; } = [];
    public IReadOnlyList<CashierOperationItemViewModel> Operations { get; init; } = [];
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

public sealed class CashierOperationItemViewModel {
    public Guid OperationId { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? AccountLastFour { get; init; }
    public string? CardLastFour { get; init; }
    public string? LoanNumber { get; init; }
    public string? RejectionCode { get; init; }
}

public sealed class DepositViewModel : IValidatableObject {
    [Required(ErrorMessage = "La cuenta destino es requerida.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "La cuenta debe contener exactamente 9 dígitos.")]
    public string AccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "El monto a depositar es requerido.")]
    public decimal? Amount { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (Amount <= 0m) {
            yield return new ValidationResult(
                "El monto a depositar debe ser mayor que cero.",
                [nameof(Amount)]
            );
        }
    }
}

public sealed class WithdrawalViewModel : IValidatableObject {
    [Required(ErrorMessage = "La cuenta origen es requerida.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "La cuenta debe contener exactamente 9 dígitos.")]
    public string AccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "El monto a retirar es requerido.")]
    public decimal? Amount { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (Amount <= 0m) {
            yield return new ValidationResult(
                "El monto a retirar debe ser mayor que cero.",
                [nameof(Amount)]
            );
        }
    }
}

public sealed class CardPaymentViewModel : IValidatableObject {
    // The application command resolves the internal card ID server-side; PAN is never bound here.
    [Range(1, int.MaxValue, ErrorMessage = "La tarjeta es requerida.")]
    public int CardId { get; set; }

    [Required(ErrorMessage = "La cuenta origen es requerida.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "La cuenta debe contener exactamente 9 dígitos.")]
    public string AccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "El monto a pagar es requerido.")]
    public decimal? Amount { get; set; }

    public IReadOnlyList<SelectOptionViewModel> CardOptions { get; init; } = [];
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (Amount <= 0m) {
            yield return new ValidationResult(
                "El monto a pagar debe ser mayor que cero.",
                [nameof(Amount)]
            );
        }
    }
}

public sealed class LoanPaymentViewModel : IValidatableObject {
    [Range(1, int.MaxValue, ErrorMessage = "El préstamo es requerido.")]
    public int LoanId { get; set; }

    [Required(ErrorMessage = "La cuenta origen es requerida.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "La cuenta debe contener exactamente 9 dígitos.")]
    public string AccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "El monto a pagar es requerido.")]
    public decimal? Amount { get; set; }

    public IReadOnlyList<SelectOptionViewModel> LoanOptions { get; init; } = [];
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (Amount <= 0m) {
            yield return new ValidationResult(
                "El monto a pagar debe ser mayor que cero.",
                [nameof(Amount)]
            );
        }
    }
}

public sealed class ThirdPartyTransferViewModel : IValidatableObject {
    [Required(ErrorMessage = "La cuenta origen es requerida.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "La cuenta debe contener exactamente 9 dígitos.")]
    public string SourceAccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "La cuenta destino es requerida.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "La cuenta debe contener exactamente 9 dígitos.")]
    public string DestinationAccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "El monto de la transacción es requerido.")]
    public decimal? Amount { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (Amount <= 0m) {
            yield return new ValidationResult(
                "El monto de la transacción debe ser mayor que cero.",
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

public sealed class TransactionResultViewModel : BaseViewModel {
    public Guid OperationId { get; init; }
    public string AccountNumber { get; init; } = string.Empty;
    public string? LoanNumber { get; init; }
    public decimal Amount { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? CardLastFour { get; init; }
    public string? NotificationWarning { get; init; }
}

public sealed class ThirdPartyTransferResultViewModel : BaseViewModel {
    public Guid OperationId { get; init; }
    public string SourceAccountNumber { get; init; } = string.Empty;
    public string DestinationAccountNumber { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? NotificationWarning { get; init; }
}
