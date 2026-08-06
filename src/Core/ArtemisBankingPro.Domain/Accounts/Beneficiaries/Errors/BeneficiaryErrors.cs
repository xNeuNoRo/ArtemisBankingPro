using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Accounts.Beneficiaries.Errors;

public static class BeneficiaryErrors {
    public static DomainError InvalidOwner { get; } =
        DomainError.Validation(
            "Beneficiary.InvalidOwner",
            "Debe indicar el titular del beneficiario."
        );

    public static DomainError InvalidDestination { get; } =
        DomainError.Validation(
            "Beneficiary.InvalidDestination",
            "Debe indicar la cuenta de destino."
        );
}
