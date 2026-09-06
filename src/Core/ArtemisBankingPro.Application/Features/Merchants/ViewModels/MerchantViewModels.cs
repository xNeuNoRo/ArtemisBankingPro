using System.ComponentModel.DataAnnotations;
using ArtemisBankingPro.Application.Common.Validation;
using ArtemisBankingPro.Application.Common.ViewModels;

namespace ArtemisBankingPro.Application.Features.Merchants.ViewModels;

public sealed class MerchantListViewModel : BaseViewModel {
    [StringLength(50, ErrorMessage = "El estado no debe exceder 50 caracteres.")]
    public string? Status { get; set; }
    public IReadOnlyList<SelectOptionViewModel> StatusOptions { get; init; } = [];
    public IReadOnlyList<MerchantSummaryViewModel> Merchants { get; init; } = [];
    public PaginationViewModel Pagination { get; init; } = new();
}

public sealed class MerchantSummaryViewModel {
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Email { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string Rnc { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool HasAssociatedUser { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class MerchantDetailViewModel : BaseViewModel {
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Email { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string Rnc { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public MerchantUserViewModel? AssociatedUser { get; init; }
}

public sealed class MerchantUserViewModel {
    public string Id { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}

public class CreateMerchantViewModel {
    [Required(ErrorMessage = "El nombre del comercio es obligatorio.")]
    [StringLength(120, ErrorMessage = "El nombre del comercio no debe exceder 120 caracteres.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "La descripción no debe exceder 500 caracteres.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
    [EmailAddress(ErrorMessage = "El correo electrónico debe tener un formato válido.")]
    [StringLength(320, ErrorMessage = "El correo electrónico no debe exceder 320 caracteres.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "El teléfono es obligatorio.")]
    [StringLength(20, ErrorMessage = "El teléfono no debe exceder 20 caracteres.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "El RNC es obligatorio.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "El RNC debe tener 9 dígitos.")]
    public string Rnc { get; set; } = string.Empty;
}

public sealed class UpdateMerchantViewModel {
    [Required(ErrorMessage = "El nombre del comercio es obligatorio.")]
    [StringLength(120, ErrorMessage = "El nombre del comercio no debe exceder 120 caracteres.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "La descripción no debe exceder 500 caracteres.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
    [EmailAddress(ErrorMessage = "El correo electrónico debe tener un formato válido.")]
    [StringLength(320, ErrorMessage = "El correo electrónico no debe exceder 320 caracteres.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "El teléfono es obligatorio.")]
    [StringLength(20, ErrorMessage = "El teléfono no debe exceder 20 caracteres.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "El RNC es obligatorio.")]
    [RegularExpression("^\\d{9}$", ErrorMessage = "El RNC debe tener 9 dígitos.")]
    public string Rnc { get; set; } = string.Empty;
}

public sealed class ChangeMerchantStatusViewModel {
    [Range(1, int.MaxValue, ErrorMessage = "El identificador del comercio es requerido.")]
    public int MerchantId { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Formulario de alta del usuario Comercio asociado. El comercio se vuelve a
/// resolver y autorizar en Application; el valor enviado nunca es ownership.
/// </summary>
public sealed class AssignCommerceUserViewModel : IValidatableObject {
    [Range(1, int.MaxValue, ErrorMessage = "El comercio es requerido.")]
    public int CommerceId { get; set; }

    [Required(ErrorMessage = "El nombre es requerido.")]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El apellido es requerido.")]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "La cédula es requerida.")]
    [StringLength(
        IdentityValidationLimits.IdentificationMaxLength,
        ErrorMessage = IdentityValidationLimits.IdentificationMaxLengthMessage
    )]
    public string Identification { get; set; } = string.Empty;

    [Required(ErrorMessage = "El correo electrónico es requerido.")]
    [EmailAddress(ErrorMessage = "El correo electrónico debe tener un formato válido.")]
    [StringLength(200)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre de usuario es requerido.")]
    [StringLength(50)]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es requerida.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "La contraseña debe tener entre 8 y 128 caracteres.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "La confirmación de la contraseña es requerida.")]
    [Compare(nameof(Password), ErrorMessage = "La contraseña y la confirmación de contraseña deben coincidir.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;

    public decimal InitialAmount { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (InitialAmount < 0m) {
            yield return new ValidationResult(
                "El balance inicial no puede ser negativo.",
                [nameof(InitialAmount)]
            );
        }
    }
}
