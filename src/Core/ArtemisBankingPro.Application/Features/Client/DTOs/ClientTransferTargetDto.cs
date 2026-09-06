namespace ArtemisBankingPro.Application.Features.Client.DTOs;

public sealed record ClientTransferTargetDto(
    string AccountNumber,
    string FirstName,
    string LastName
);
