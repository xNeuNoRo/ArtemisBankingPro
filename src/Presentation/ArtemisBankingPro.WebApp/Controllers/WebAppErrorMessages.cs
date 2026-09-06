using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.WebApp.Controllers;

/// <summary>
/// Converts known domain outcomes to MVC copy without trusting domain or
/// infrastructure error descriptions at the browser boundary.
/// </summary>
internal static class WebAppErrorMessages {
    public static string For(DomainError? error, string fallback) => error?.Code switch {
        "User.UserNameExists" => "Ya existe un usuario registrado con este nombre de usuario.",
        "User.EmailExists" => "Ya existe un usuario registrado con este correo electrónico.",
        "User.IdentificationExists" => "Ya existe un usuario registrado con esta cédula.",
        "User.Duplicate" => "Ya existe un usuario registrado con alguno de los datos indicados.",
        "User.NotFound" => "El usuario seleccionado no existe.",
        "User.NoPrincipalAccount" => "El usuario no tiene una cuenta de ahorro principal activa.",
        "User.PasswordChangeFailed" => "No fue posible cambiar la contraseña del usuario.",
        "User.RoleAssignmentFailed" => "No fue posible asignar el rol al usuario.",
        "User.RoleNotFound" => "El rol indicado no está configurado.",
        "User.OperationFailed" => "No fue posible completar la operación del usuario.",
        "User.StatusUpdateFailed" => "No fue posible actualizar el estado del usuario.",
        "Account.CustomerNotFound" => "No existe un cliente con este identificador.",
        "Account.CustomerNotActive" => "El cliente está inactivo y no puede recibir una cuenta secundaria.",
        "Account.CustomerNotClient" => "El identificador no corresponde a un cliente con rol Cliente.",
        "Account.NoPrincipalAccount" => "El cliente no tiene una cuenta de ahorro principal activa.",
        "Account.NotFound" => "La cuenta de ahorro seleccionada no existe.",
        "Account.NotActive" => "La cuenta de ahorro no está activa.",
        "Account.PrimaryCannotBeCancelled" => "La cuenta de ahorro principal no se puede cancelar.",
        "Account.BalanceMustBeZero" => "El balance debe ser cero antes de cancelar.",
        "Account.NoBalanceToTransfer" => "La cuenta no tiene balance para transferir.",
        "Account.PrincipalRequired" => "Se requiere la cuenta principal del cliente para recibir el balance transferido.",
        "Account.InvalidPrincipalForTransfer" => "El balance debe transferirse a la cuenta principal del mismo cliente.",
        "Account.PrincipalNotActive" => "La cuenta principal debe estar activa para recibir el balance transferido.",
        "SavingsAccount.CustomerNotFound"
            or "Loan.CustomerNotFound"
            or "Card.CustomerNotFound"
            => "No existe un cliente registrado con esta cédula.",
        "Loan.ActiveLoanExists" => "Este cliente ya tiene un préstamo activo asignado.",
        "Loan.NoPrincipalAccount" => "El cliente no tiene una cuenta de ahorro principal activa para recibir el desembolso del préstamo.",
        "Loan.NotFound" => "El préstamo seleccionado no existe.",
        "Loan.NotActive" => "El préstamo no está activo.",
        "Loan.NoPendingInstallments" => "El préstamo no tiene cuotas pendientes por pagar.",
        "Loan.InvalidRequest" => "El usuario seleccionado no es un cliente.",
        "Card.CustomerNotActive" => "El cliente está inactivo y no puede recibir una tarjeta de crédito.",
        "Card.CustomerNotClient" => "El identificador no corresponde a un cliente con rol Cliente.",
        "Card.NoPrincipalAccount" => "El cliente no tiene una cuenta de ahorro principal activa.",
        "Card.NotFound" => "La tarjeta de crédito seleccionada no existe.",
        "Card.NotActive" => "La tarjeta de crédito no está activa.",
        "Card.Expired" => "La tarjeta de crédito está vencida.",
        "Card.NoDebt" => "La tarjeta de crédito no tiene deuda pendiente.",
        "Card.LimitBelowDebt" => "El límite de crédito no puede ser menor que la deuda actual.",
        "Card.DebtMustBeZero" => "La deuda de la tarjeta debe ser cero antes de cancelar.",
        "Confirmation.Required" => "La confirmación de alto riesgo es requerida.",
        "Confirmation.Invalid" => "La confirmación no corresponde a esta operación.",
        "Concurrency.Conflict" => "La operación entró en conflicto con otra operación. Intente nuevamente.",
        _ => fallback,
    };

    public static string HighRisk(DomainError? error) {
        if (error?.Code != "Loan.HighRiskConfirmationRequired") {
            return "La deuda del cliente supera el umbral promedio del sistema.";
        }

        string? riskType = error.Extensions?.TryGetValue("riskType", out object? value) == true
            ? value as string
            : null;
        return string.Equals(riskType, "CurrentHighRisk", StringComparison.Ordinal)
            ? "Este cliente se considera de alto riesgo, ya que su deuda actual supera el promedio del sistema."
            : "Asignar este préstamo convertirá al cliente en un cliente de alto riesgo, ya que su deuda superará el umbral promedio del sistema.";
    }
}
