using ArtemisBankingPro.Domain.Common.Pagination;

namespace ArtemisBankingPro.Application.Features.Admin.DTOs;

public sealed record EligibleClientDto(
    string ClientId,
    string Identification,
    string FullName,
    string Email,
    decimal TotalDebt
);

public sealed record EligibleClientsResponse(
    PageResult<EligibleClientDto> Clients,
    decimal AverageDebt
);
