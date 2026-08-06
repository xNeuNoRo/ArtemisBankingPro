using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Cards.Errors;

public static class CardErrors {
    public static DomainError InvalidNumber { get; } =
        DomainError.Validation(
            "Card.InvalidNumber",
            "El número de tarjeta debe tener exactamente dieciséis dígitos."
        );

    public static DomainError InvalidExpiration { get; } =
        DomainError.Validation(
            "Card.InvalidExpiration",
            "El mes y año de expiración de la tarjeta no son válidos."
        );

    public static DomainError InvalidCustomer { get; } =
        DomainError.Validation("Card.InvalidCustomer", "Debe indicar el cliente de la tarjeta.");

    public static DomainError InvalidAssigner { get; } =
        DomainError.Validation(
            "Card.InvalidAssigner",
            "Debe indicar el administrador que asigna la tarjeta."
        );

    public static DomainError InvalidCvcDigest { get; } =
        DomainError.Validation(
            "Card.InvalidCvcDigest",
            "Se requiere el código de seguridad CVC en formato SHA-256."
        );

    public static DomainError InconsistentIssueDate { get; } =
        DomainError.Validation(
            "Card.InconsistentIssueDate",
            "La fecha de emisión y la fecha del negocio deben coincidir."
        );

    public static DomainError InvalidCancellationDate { get; } =
        DomainError.Validation(
            "Card.InvalidCancellationDate",
            "La fecha de cancelación no puede ser anterior a la de emisión."
        );

    public static DomainError LimitMustBePositive { get; } =
        DomainError.Validation(
            "Card.LimitMustBePositive",
            "El límite de crédito debe ser mayor a cero."
        );

    public static DomainError AmountMustBePositive { get; } =
        DomainError.Validation("Card.AmountMustBePositive", "El monto debe ser mayor a cero.");

    public static DomainError NotActive { get; } =
        DomainError.Conflict("Card.NotActive", "La tarjeta de crédito no está activa.");

    public static DomainError Expired { get; } =
        DomainError.Declined("Card.Expired", "La tarjeta de crédito está vencida.");

    public static DomainError InsufficientCredit { get; } =
        DomainError.Declined(
            "Card.InsufficientCredit",
            "La tarjeta de crédito no tiene crédito disponible suficiente."
        );

    public static DomainError NoDebt { get; } =
        DomainError.Conflict("Card.NoDebt", "La tarjeta de crédito no tiene deuda pendiente.");

    public static DomainError LimitBelowDebt { get; } =
        DomainError.Conflict(
            "Card.LimitBelowDebt",
            "El límite de crédito no puede ser menor que la deuda actual."
        );

    public static DomainError DebtMustBeZero { get; } =
        DomainError.Conflict(
            "Card.DebtMustBeZero",
            "La deuda de la tarjeta debe ser cero antes de cancelar."
        );

    public static DomainError InvalidConsumption { get; } =
        DomainError.Validation(
            "CardConsumption.Invalid",
            "Los datos del consumo de la tarjeta no son válidos."
        );

    public static DomainError InvalidCashAdvanceMerchant { get; } =
        DomainError.Validation(
            "CardConsumption.InvalidCashAdvanceMerchant",
            "Un avance de efectivo debe usar la descripción AVANCE y ningún comercio."
        );

    public static DomainError InvalidPurchaseMerchant { get; } =
        DomainError.Validation(
            "CardConsumption.InvalidPurchaseMerchant",
            "Una compra requiere un comercio."
        );
}
