using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

public sealed class GetMyBeneficiariesQueryHandler
    : IRequestHandler<GetMyBeneficiariesQuery, Result<IReadOnlyList<MyBeneficiaryDto>>> {
    private readonly IBeneficiaryRepository _beneficiaryRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUserService _currentUser;

    public GetMyBeneficiariesQueryHandler(
        IBeneficiaryRepository beneficiaryRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IUserRepository userRepository,
        ICurrentUserService currentUser
    ) {
        _beneficiaryRepository = beneficiaryRepository;
        _savingsAccountRepository = savingsAccountRepository;
        _userRepository = userRepository;
        _currentUser = currentUser;
    }

    public async ValueTask<Result<IReadOnlyList<MyBeneficiaryDto>>> Handle(
        GetMyBeneficiariesQuery message,
        CancellationToken cancellationToken
    ) {
        IReadOnlyList<Beneficiary> beneficiaries =
            await _beneficiaryRepository.GetByOwnerAsync(
                _currentUser.UserId!,
                cancellationToken
            );
        IReadOnlyList<SavingsAccount> accounts = await _savingsAccountRepository.GetByIdsAsync(
            beneficiaries.Select(beneficiary => beneficiary.DestinationAccountId).ToList(),
            cancellationToken
        );
        var accountsById = accounts
            .Where(account => account.Status == AccountStatus.Active)
            .ToDictionary(account => account.Id);
        List<(Beneficiary Beneficiary, SavingsAccount Account)> resolved = beneficiaries
            .Where(beneficiary => accountsById.ContainsKey(beneficiary.DestinationAccountId))
            .Select(beneficiary => (beneficiary, accountsById[beneficiary.DestinationAccountId]))
            .ToList();

        var users = await _userRepository.GetByIdsAsync(
            resolved.Select(item => item.Account.OwnerUserId).Distinct().ToList(),
            cancellationToken
        );
        var usersById = users.ToDictionary(user => user.Id);
        IReadOnlyList<MyBeneficiaryDto> result = resolved
            .Select(item => {
                usersById.TryGetValue(item.Account.OwnerUserId, out var user);
                return new MyBeneficiaryDto(
                    item.Beneficiary.Id,
                    user?.FirstName ?? string.Empty,
                    user?.LastName ?? string.Empty,
                    item.Account.Number.Value
                );
            })
            .ToList();

        return Result.Success(result);
    }
}
