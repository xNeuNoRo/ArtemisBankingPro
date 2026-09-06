using System.ComponentModel.DataAnnotations;
using ArtemisBankingPro.Application.Common.ViewModels;

namespace ArtemisBankingPro.Application.Features.CreditCard.ViewModels;

public sealed class CreditCardListViewModel : BaseViewModel {
    [StringLength(50, ErrorMessage = "El estado no debe exceder 50 caracteres.")]
    public string? Status { get; set; }

    [StringLength(20, ErrorMessage = "La cédula no debe exceder 20 caracteres.")]
    public string? Identification { get; set; }

    public IReadOnlyList<SelectOptionViewModel> StatusOptions { get; init; } = [];
    public IReadOnlyList<CreditCardSummaryViewModel> Cards { get; init; } = [];
    public PaginationViewModel Pagination { get; init; } = new();
}

public sealed class CreditCardSummaryViewModel {
    public int Id { get; init; }
    public string MaskedNumber { get; init; } = string.Empty;
    public string LastFour { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string ClientFullName { get; init; } = string.Empty;
    public decimal CreditLimit { get; init; }
    public decimal AvailableCredit { get; init; }
    public decimal CurrentDebt { get; init; }
    public string Expiration { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class CreditCardDetailViewModel : BaseViewModel {
    public int Id { get; init; }
    public string MaskedNumber { get; init; } = string.Empty;
    public string LastFour { get; init; } = string.Empty;
    public string ClientFullName { get; init; } = string.Empty;
    public decimal CreditLimit { get; init; }
    public decimal AvailableCredit { get; init; }
    public decimal CurrentDebt { get; init; }
    public string Expiration { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public IReadOnlyList<CardConsumptionViewModel> Consumptions { get; init; } = [];
    public PaginationViewModel Pagination { get; init; } = new();
}

public sealed class CardConsumptionViewModel {
    public int Id { get; init; }
    public DateTimeOffset Date { get; init; }
    public decimal Amount { get; init; }
    public string CommerceName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

public sealed class AssignCreditCardViewModel : IValidatableObject {
    [Required(ErrorMessage = "El límite de crédito es requerido.")]
    public decimal? CreditLimit { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (CreditLimit <= 0m) {
            yield return new ValidationResult(
                "El límite de crédito debe ser mayor que cero.",
                [nameof(CreditLimit)]
            );
        }
    }
}

public sealed class UpdateCardLimitViewModel : IValidatableObject {
    [Required(ErrorMessage = "El nuevo límite de crédito es requerido.")]
    public decimal? NewLimit { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (NewLimit <= 0m) {
            yield return new ValidationResult(
                "El límite de la tarjeta debe ser mayor que cero.",
                [nameof(NewLimit)]
            );
        }
    }
}

public sealed class CancelCreditCardViewModel : ConfirmationViewModel {
    public string LastFour { get; init; } = string.Empty;
}
