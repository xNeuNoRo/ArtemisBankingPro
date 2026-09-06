using System.ComponentModel.DataAnnotations;
using ArtemisBankingPro.Application.Common.ViewModels;

namespace ArtemisBankingPro.Application.Features.Users.ViewModels;

public sealed class UserListViewModel : BaseViewModel {
    [StringLength(50, ErrorMessage = "El rol no debe exceder 50 caracteres.")]
    public string? Role { get; set; }
    public IReadOnlyList<SelectOptionViewModel> RoleOptions { get; init; } = [];
    public IReadOnlyList<UserListItemViewModel> Users { get; init; } = [];
    public PaginationViewModel Pagination { get; init; } = new();
}

public sealed class UserListItemViewModel {
    public string UserId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Identification { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class UserDetailViewModel : BaseViewModel {
    public string UserId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Identification { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public UserMainAccountViewModel? MainAccount { get; init; }
}

public sealed class UserMainAccountViewModel {
    public string AccountNumber { get; init; } = string.Empty;
    public decimal Balance { get; init; }
    public bool IsPrincipal { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed class CreateUserViewModel : IValidatableObject {
    [Required(ErrorMessage = "El nombre es requerido.")]
    [StringLength(100, ErrorMessage = "El nombre no debe exceder 100 caracteres.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El apellido es requerido.")]
    [StringLength(100, ErrorMessage = "El apellido no debe exceder 100 caracteres.")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "La cédula es requerida.")]
    [StringLength(20, ErrorMessage = "La cédula no debe exceder 20 caracteres.")]
    public string Identification { get; set; } = string.Empty;

    [Required(ErrorMessage = "El correo electrónico es requerido.")]
    [EmailAddress(ErrorMessage = "El correo electrónico debe tener un formato válido.")]
    [StringLength(200, ErrorMessage = "El correo no debe exceder 200 caracteres.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre de usuario es requerido.")]
    [StringLength(50, ErrorMessage = "El nombre de usuario no debe exceder 50 caracteres.")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es requerida.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "La contraseña debe tener entre 8 y 128 caracteres.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "La confirmación de la contraseña es requerida.")]
    [Compare(nameof(Password), ErrorMessage = "La contraseña y la confirmación de contraseña deben coincidir.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "El tipo de usuario es requerido.")]
    [RegularExpression("^(Administrador|Cajero|Cliente)$", ErrorMessage = "El tipo de usuario debe ser Administrador, Cajero o Cliente.")]
    public string Role { get; set; } = string.Empty;

    public decimal? InitialAmount { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (InitialAmount < 0m) {
            yield return new ValidationResult(
                "El monto inicial no puede ser negativo.",
                [nameof(InitialAmount)]
            );
        }

        if (!string.Equals(Role, "Cliente", StringComparison.Ordinal)
            && InitialAmount is not null) {
            yield return new ValidationResult(
                "El monto inicial solo aplica para usuarios con rol Cliente.",
                [nameof(InitialAmount)]
            );
        }
    }
}

public sealed class CreateCommerceUserViewModel : IValidatableObject {
    [Range(1, int.MaxValue, ErrorMessage = "El comercio es requerido.")]
    public int CommerceId { get; set; }

    [Required(ErrorMessage = "El nombre es requerido.")]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El apellido es requerido.")]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "La cédula es requerida.")]
    [StringLength(20)]
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

public sealed class UpdateUserViewModel : IValidatableObject {
    [Required(ErrorMessage = "El nombre es requerido.")]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El apellido es requerido.")]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "La cédula es requerida.")]
    [StringLength(20)]
    public string Identification { get; set; } = string.Empty;

    [Required(ErrorMessage = "El correo electrónico es requerido.")]
    [EmailAddress(ErrorMessage = "El correo electrónico debe tener un formato válido.")]
    [StringLength(200)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre de usuario es requerido.")]
    [StringLength(50)]
    public string UserName { get; set; } = string.Empty;

    [StringLength(128, MinimumLength = 8, ErrorMessage = "La contraseña debe tener entre 8 y 128 caracteres.")]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [Compare(nameof(Password), ErrorMessage = "La contraseña y la confirmación de contraseña deben coincidir.")]
    [DataType(DataType.Password)]
    public string? ConfirmPassword { get; set; }

    public decimal? AdditionalAmount { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
        if (AdditionalAmount < 0m) {
            yield return new ValidationResult(
                "El monto adicional no puede ser negativo.",
                [nameof(AdditionalAmount)]
            );
        }

        if (!string.IsNullOrWhiteSpace(Password)
            && string.IsNullOrWhiteSpace(ConfirmPassword)) {
            yield return new ValidationResult(
                "Debe confirmar la nueva contraseña.",
                [nameof(ConfirmPassword)]
            );
        }
    }
}

public sealed class ChangeUserStatusViewModel {
    [Required(ErrorMessage = "El identificador del usuario es requerido.")]
    [StringLength(450, ErrorMessage = "El identificador del usuario no es válido.")]
    public string UserId { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
