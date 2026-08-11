using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Accounts.Errors;

public static class AccountErrors {
    public static DomainError InvalidNumber { get; } =
        DomainError.Validation(
            "Account.InvalidNumber",
            "El número de cuenta debe tener exactamente nueve dígitos."
        );

    public static DomainError InvalidOwner { get; } =
        DomainError.Validation("Account.InvalidOwner", "Debe indicar el titular de la cuenta.");

    public static DomainError InvalidCreator { get; } =
        DomainError.Validation("Account.InvalidCreator", "Debe indicar quién crea la cuenta.");

    public static DomainError InvalidAccountData { get; } =
        DomainError.Validation(
            "Account.InvalidData",
            "El número de cuenta y el balance inicial son obligatorios."
        );

    public static DomainError InvalidCancellationDate { get; } =
        DomainError.Validation(
            "Account.InvalidCancellationDate",
            "La fecha de cancelación no puede ser anterior a la de apertura."
        );

    public static DomainError AmountMustBePositive { get; } =
        DomainError.Validation("Account.AmountMustBePositive", "El monto debe ser mayor a cero.");

    public static DomainError NotActive { get; } =
        DomainError.Conflict("Account.NotActive", "La cuenta de ahorro no está activa.");

    public static DomainError SourceNotFound { get; } =
        DomainError.NotFound(
            "Account.SourceNotFound",
            "No existe una cuenta de ahorro con el número indicado como origen."
        );

    public static DomainError DestinationNotFound { get; } =
        DomainError.NotFound(
            "Account.DestinationNotFound",
            "No existe una cuenta de ahorro con el número indicado como destino."
        );

    public static DomainError InsufficientFunds { get; } =
        DomainError.Declined(
            "Account.InsufficientFunds",
            "La cuenta de ahorro no tiene fondos suficientes."
        );

    public static DomainError PrimaryCannotBeCancelled { get; } =
        DomainError.Conflict(
            "Account.PrimaryCannotBeCancelled",
            "La cuenta de ahorro principal no se puede cancelar."
        );

    public static DomainError BalanceMustBeZero { get; } =
        DomainError.Conflict(
            "Account.BalanceMustBeZero",
            "El balance debe ser cero antes de cancelar."
        );

    public static DomainError NoBalanceToTransfer { get; } =
        DomainError.Conflict(
            "Account.NoBalanceToTransfer",
            "La cuenta no tiene balance para transferir."
        );

    public static DomainError PrincipalRequired { get; } =
        DomainError.Validation(
            "Account.PrincipalRequired",
            "Se requiere la cuenta principal del cliente para recibir el balance transferido."
        );

    public static DomainError InvalidPrincipalForTransfer { get; } =
        DomainError.Conflict(
            "Account.InvalidPrincipalForTransfer",
            "El balance debe transferirse a la cuenta principal del mismo cliente."
        );

    public static DomainError PrincipalNotActive { get; } =
        DomainError.Conflict(
            "Account.PrincipalNotActive",
            "La cuenta principal debe estar activa para recibir el balance transferido."
        );

    public static DomainError InvalidTransactionReference { get; } =
        DomainError.Validation(
            "AccountTransaction.InvalidReference",
            "Las referencias de la transacción no son válidas."
        );

    public static DomainError InvalidTransactionAmount { get; } =
        DomainError.Validation(
            "AccountTransaction.InvalidAmount",
            "El monto y la dirección de la transacción no son válidos."
        );

    public static DomainError InvalidTransactionDescription { get; } =
        DomainError.Validation(
            "AccountTransaction.InvalidDescription",
            "Se requieren las referencias de origen y beneficiario."
        );
}
