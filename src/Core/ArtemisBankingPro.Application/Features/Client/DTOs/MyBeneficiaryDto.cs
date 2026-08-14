namespace ArtemisBankingPro.Application.Features.Client.DTOs;

public sealed record MyBeneficiaryDto(
    int BeneficiaryId,
    string FirstName,
    string LastName,
    string AccountNumber
);
