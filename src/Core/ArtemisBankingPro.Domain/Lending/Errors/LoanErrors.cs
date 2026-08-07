using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Lending.Errors;

public static class LoanErrors {
    public static DomainError InvalidNumber { get; } =
        DomainError.Validation(
            "Loan.InvalidNumber",
            "El número de préstamo debe tener exactamente nueve dígitos."
        );

    public static DomainError InvalidCustomer { get; } =
        DomainError.Validation("Loan.InvalidCustomer", "Debe indicar el cliente del préstamo.");

    public static DomainError InvalidAssigner { get; } =
        DomainError.Validation(
            "Loan.InvalidAssigner",
            "Debe indicar el administrador que asigna el préstamo."
        );

    public static DomainError InvalidLoanData { get; } =
        DomainError.Validation(
            "Loan.InvalidData",
            "El número de préstamo y la tasa de interés anual son obligatorios."
        );

    public static DomainError InconsistentIssueDate { get; } =
        DomainError.Validation(
            "Loan.InconsistentIssueDate",
            "La fecha de emisión y la fecha del negocio deben coincidir."
        );

    public static DomainError InvalidPaymentDate { get; } =
        DomainError.Validation(
            "Loan.InvalidPaymentDate",
            "La fecha de pago no puede ser anterior a la de emisión."
        );

    public static DomainError PrincipalMustBePositive { get; } =
        DomainError.Validation(
            "Loan.PrincipalMustBePositive",
            "El capital aprobado debe ser mayor a cero."
        );

    public static DomainError PrincipalTooSmallForTerm { get; } =
        DomainError.Validation(
            "Loan.PrincipalTooSmallForTerm",
            "El capital es muy pequeño para el plazo seleccionado con precisión de dos decimales."
        );

    public static DomainError UnsupportedInterestRate { get; } =
        DomainError.Validation(
            "Loan.UnsupportedInterestRate",
            "La tasa de interés no se puede calcular de forma segura para el plazo seleccionado."
        );

    public static DomainError InvalidTerm { get; } =
        DomainError.Validation(
            "Loan.InvalidTerm",
            "El plazo del préstamo debe ser entre 6 y 60 meses, en intervalos de 6 meses."
        );

    public static DomainError NegativeInterestRate { get; } =
        DomainError.Validation(
            "Loan.NegativeInterestRate",
            "La tasa de interés anual no puede ser negativa."
        );

    public static DomainError NotActive { get; } =
        DomainError.Conflict("Loan.NotActive", "El préstamo no está activo.");

    public static DomainError PaymentMustBePositive { get; } =
        DomainError.Validation(
            "Loan.PaymentMustBePositive",
            "El monto del pago debe ser mayor a cero."
        );

    public static DomainError NoEligibleInstallments { get; } =
        DomainError.Conflict(
            "Loan.NoEligibleInstallments",
            "El préstamo no tiene cuotas futuras pendientes elegibles para recálculo."
        );
}
