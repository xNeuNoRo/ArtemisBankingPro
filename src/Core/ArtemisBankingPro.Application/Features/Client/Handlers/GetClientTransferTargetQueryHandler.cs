using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

public sealed class GetClientTransferTargetQueryHandler
    : IRequestHandler<GetClientTransferTargetQuery, Result<ClientTransferTargetDto>> {
    private readonly ISavingsAccountRepository _savingsAccounts;
    private readonly IUserRepository _users;

    public GetClientTransferTargetQueryHandler(
        ISavingsAccountRepository savingsAccounts,
        IUserRepository users
    ) {
        _savingsAccounts = savingsAccounts;
        _users = users;
    }

    public async ValueTask<Result<ClientTransferTargetDto>> Handle(
        GetClientTransferTargetQuery message,
        CancellationToken cancellationToken
    ) {
        Result<AccountNumber> number = AccountNumber.Create(message.DestinationAccountNumber);
        if (number.IsFailure) {
            return Result.Failure<ClientTransferTargetDto>(
                DomainError.Validation(
                    "Client.TransferTarget.InvalidNumber",
                    "El número de cuenta ingresado no corresponde a una cuenta válida."
                )
            );
        }

        var account = await _savingsAccounts.GetByNumberAsync(number.Value, cancellationToken);
        if (account is null || account.Status != AccountStatus.Active) {
            return Result.Failure<ClientTransferTargetDto>(
                DomainError.Validation(
                    "Client.TransferTarget.Unavailable",
                    "El número de cuenta ingresado no corresponde a una cuenta válida."
                )
            );
        }

        var owner = await _users.GetByIdAsync(account.OwnerUserId, cancellationToken);
        return owner is null
            ? Result.Failure<ClientTransferTargetDto>(
                DomainError.Validation(
                    "Client.TransferTarget.Unavailable",
                    "El número de cuenta ingresado no corresponde a una cuenta válida."
                )
            )
            : Result.Success(
                new ClientTransferTargetDto(
                    account.Number.Value,
                    owner.FirstName,
                    owner.LastName
                )
            );
    }
}
