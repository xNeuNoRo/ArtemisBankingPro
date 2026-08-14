using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
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
        List<(Beneficiary Beneficiary, SavingsAccount Account)> resolved = [];
        foreach (Beneficiary beneficiary in beneficiaries) {
            SavingsAccount? account = await _savingsAccountRepository.GetByIdAsync(
                beneficiary.DestinationAccountId,
                cancellationToken
            );
            if (account is not null) {
                resolved.Add((beneficiary, account));
            }
        }

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
