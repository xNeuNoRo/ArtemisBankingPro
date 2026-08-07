using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Merchants.Errors;

public static class MerchantErrors {
    public static DomainError InvalidName { get; } =
        DomainError.Validation("Merchant.InvalidName", "Debe indicar el nombre del comercio.");

    public static DomainError InvalidEmail { get; } =
        DomainError.Validation(
            "Merchant.InvalidEmail",
            "Debe indicar un correo electrónico válido para el comercio."
        );

    public static DomainError InvalidPhoneNumber { get; } =
        DomainError.Validation(
            "Merchant.InvalidPhoneNumber",
            "Debe indicar el número de teléfono del comercio."
        );

    public static DomainError InvalidRnc { get; } =
        DomainError.Validation("Merchant.InvalidRnc", "Debe indicar el RNC del comercio.");

    public static DomainError InvalidCreator { get; } =
        DomainError.Validation(
            "Merchant.InvalidCreator",
            "Debe indicar el administrador que crea el comercio."
        );

    public static DomainError UserAlreadyAssociated { get; } =
        DomainError.Conflict(
            "Merchant.UserAlreadyAssociated",
            "El comercio ya tiene un usuario asociado."
        );

    public static DomainError InvalidAssociatedUser { get; } =
        DomainError.Validation(
            "Merchant.InvalidAssociatedUser",
            "Debe indicar un usuario de comercio."
        );

    public static DomainError AlreadyActive { get; } =
        DomainError.Conflict("Merchant.AlreadyActive", "El comercio ya está activo.");

    public static DomainError AlreadyInactive { get; } =
        DomainError.Conflict("Merchant.AlreadyInactive", "El comercio ya está inactivo.");

    public static DomainError InvalidUpdateDate { get; } =
        DomainError.Validation(
            "Merchant.InvalidUpdateDate",
            "La fecha de actualización no puede ser anterior a la de creación."
        );
}
