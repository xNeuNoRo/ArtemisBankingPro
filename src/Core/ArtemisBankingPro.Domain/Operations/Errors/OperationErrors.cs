using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Operations.Errors;

public static class OperationErrors {
    public static DomainError InvalidId { get; } =
        DomainError.Validation("Operation.InvalidId", "Se requiere un identificador de operación financiera.");

    public static DomainError InvalidKind { get; } =
        DomainError.Validation("Operation.InvalidKind", "El tipo de operación financiera no es válido.");

    public static DomainError InvalidActor { get; } =
        DomainError.Validation("Operation.InvalidActor", "Se requiere el actor de la operación financiera.");

    public static DomainError InvalidRequestedAmount { get; } =
        DomainError.Validation("Operation.InvalidRequestedAmount", "El monto solicitado debe ser mayor a cero.");

    public static DomainError InvalidAppliedAmount { get; } =
        DomainError.Validation("Operation.InvalidAppliedAmount", "Una operación aprobada debe aplicar un monto positivo.");

    public static DomainError RejectedOperationAppliedFunds { get; } =
        DomainError.Conflict("Operation.RejectedOperationAppliedFunds", "Una operación rechazada no puede aplicar fondos.");

    public static DomainError MissingRejectionCode { get; } =
        DomainError.Validation("Operation.MissingRejectionCode", "Una operación rechazada requiere un código de rechazo.");

    public static DomainError InvalidAmountEquation { get; } =
        DomainError.Conflict("Operation.InvalidAmountEquation", "Los montos solicitado, aplicado, de interés y de detalle no son consistentes.");

    public static DomainError InvalidDetails { get; } =
        DomainError.Conflict("Operation.InvalidDetails", "Los detalles de la operación financiera no coinciden con su tipo y estado.");

    public static DomainError InvalidProductReference { get; } =
        DomainError.Conflict("Operation.InvalidProductReference", "Las referencias de productos financieros no coinciden con el tipo de operación.");

    public static DomainError UnbalancedTransfer { get; } =
        DomainError.Conflict("Operation.UnbalancedTransfer", "Una transferencia requiere movimientos de débito y crédito iguales para cuentas diferentes.");
}
