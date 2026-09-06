using ArtemisBankingPro.Application.Features.Admin.DTOs;
using ArtemisBankingPro.Application.Features.Admin.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Admin.Handlers;

public sealed class GetEligibleClientsQueryHandler
    : IRequestHandler<GetEligibleClientsQuery, Result<EligibleClientsResponse>> {
    private readonly IAdminRepository _adminRepository;
    private readonly IUserRepository _userRepository;

    public GetEligibleClientsQueryHandler(
        IAdminRepository adminRepository,
        IUserRepository userRepository
    ) {
        _adminRepository = adminRepository;
        _userRepository = userRepository;
    }

    public async ValueTask<Result<EligibleClientsResponse>> Handle(
        GetEligibleClientsQuery message,
        CancellationToken cancellationToken
    ) {
        IReadOnlyList<string> activeClientIds = await _userRepository.GetActiveClientIdsAsync(
            cancellationToken
        );
        IReadOnlyDictionary<string, ClientAssignmentFinancialFacts> facts =
            await _adminRepository.GetClientAssignmentFactsAsync(
                activeClientIds,
                cancellationToken
            );

        IReadOnlyCollection<string> eligibleClientIds = facts
            .Where(pair => IsEligible(message.Product, pair.Value))
            .Select(pair => pair.Key)
            .ToArray();
        if (!string.IsNullOrWhiteSpace(message.SelectedClientId)) {
            eligibleClientIds = eligibleClientIds
                .Where(clientId => string.Equals(
                    clientId,
                    message.SelectedClientId,
                    StringComparison.Ordinal
                ))
                .ToArray();
        }
        PageResult<UserListDto> users = await _userRepository.GetActiveClientsPagedAsync(
            eligibleClientIds,
            message.Identification,
            new PageRequest(message.Page, message.PageSize),
            cancellationToken
        );

        decimal averageDebt = activeClientIds.Count == 0
            ? 0m
            : Money.Create(facts.Values.Sum(fact => fact.TotalDebt) / activeClientIds.Count)
                .Value.Amount;

        var page = new PageResult<EligibleClientDto>(
            users.Items
                .Select(item => new EligibleClientDto(
                    item.Id,
                    item.Identification,
                    $"{item.FirstName} {item.LastName}".Trim(),
                    item.Email,
                    facts[item.Id].TotalDebt
                ))
                .ToList(),
            users.TotalCount,
            message.Page,
            message.PageSize
        );

        return Result.Success(new EligibleClientsResponse(page, averageDebt));
    }

    private static bool IsEligible(
        ClientAssignmentProduct product,
        ClientAssignmentFinancialFacts facts
    ) => product switch {
        ClientAssignmentProduct.Loan => !facts.HasActiveLoan,
        ClientAssignmentProduct.CreditCard => true,
        ClientAssignmentProduct.SecondarySavingsAccount => facts.HasPrincipalSavingsAccount,
        _ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
    };
}
