using ArtemisBankingPro.Application.Features.Users.DTOs;
using ArtemisBankingPro.Application.Features.Users.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Users.Handlers;

/// <summary>
/// Detalle de un usuario con su cuenta de ahorro principal (si existe).
/// </summary>
public sealed class GetUserByIdQueryHandler
    : IRequestHandler<GetUserByIdQuery, Result<UserDetailResponse>> {
    private readonly IUserRepository _userRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;

    public GetUserByIdQueryHandler(
        IUserRepository userRepository,
        ISavingsAccountRepository savingsAccountRepository
    ) {
        _userRepository = userRepository;
        _savingsAccountRepository = savingsAccountRepository;
    }

    public async ValueTask<Result<UserDetailResponse>> Handle(
        GetUserByIdQuery message,
        CancellationToken cancellationToken
    ) {
        var user = await _userRepository.GetByIdAsync(message.UserId, cancellationToken);
        if (user is null) {
            return Result.Failure<UserDetailResponse>(
                DomainError.NotFound(
                    "User.NotFound",
                    "El usuario indicado no existe."
                )
            );
        }

        var principalAccount = await _savingsAccountRepository.GetPrincipalByOwnerAsync(
            message.UserId,
            cancellationToken
        );

        return Result.Success(
            new UserDetailResponse(
                user.Id,
                user.UserName,
                user.Identification,
                user.FirstName,
                user.LastName,
                user.Email,
                user.Role,
                user.IsActive,
                user.CreatedAt,
                principalAccount is null
                    ? null
                    : new UserMainAccountResponse(
                        principalAccount.Number.Value,
                        principalAccount.Balance.Amount,
                        principalAccount.Type == AccountType.Primary,
                        principalAccount.Status.ToString()
                    )
            )
        );
    }
}
