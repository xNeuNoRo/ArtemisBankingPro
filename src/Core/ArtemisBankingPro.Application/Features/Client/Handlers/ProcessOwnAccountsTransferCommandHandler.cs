using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Application.Features.FinancialProcessors;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Errors;
using Mediator;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

public sealed class ProcessOwnAccountsTransferCommandHandler
    : IRequestHandler<ProcessOwnAccountsTransferCommand, Result<Unit>> {
    private readonly ITransferProcessor _processor;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessOwnAccountsTransferCommandHandler> _logger;

    public ProcessOwnAccountsTransferCommandHandler(
        ITransferProcessor processor,
        ISavingsAccountRepository savingsAccountRepository,
        IUserRepository userRepository,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessOwnAccountsTransferCommandHandler> logger
    ) {
        _processor = processor;
        _savingsAccountRepository = savingsAccountRepository;
        _userRepository = userRepository;
        _clock = clock;
        _currentUser = currentUser;
        _emailService = emailService;
        _logger = logger;
    }

    public async ValueTask<Result<Unit>> Handle(
        ProcessOwnAccountsTransferCommand message,
        CancellationToken cancellationToken
    ) {
        Result<AccountNumber> sourceNumber = AccountNumber.Create(message.SourceAccountNumber);
        if (sourceNumber.IsFailure) {
            return Result.Failure<Unit>(sourceNumber.Error!);
        }

        Result<AccountNumber> destinationNumber = AccountNumber.Create(
            message.DestinationAccountNumber
        );
        if (destinationNumber.IsFailure) {
            return Result.Failure<Unit>(destinationNumber.Error!);
        }

        if (sourceNumber.Value == destinationNumber.Value) {
            return Result.Failure<Unit>(OperationErrors.SameAccount);
        }

        Result<Money> amountResult = Money.Create(message.Amount);
        if (amountResult.IsFailure) {
            return Result.Failure<Unit>(amountResult.Error!);
        }

        SavingsAccount? source = await _savingsAccountRepository.GetByNumberAsync(
            sourceNumber.Value,
            cancellationToken
        );
        SavingsAccount? destination = await _savingsAccountRepository.GetByNumberAsync(
            destinationNumber.Value,
            cancellationToken
        );
        if (source is null) {
            return Result.Failure<Unit>(AccountErrors.SourceNotFound);
        }

        if (destination is null) {
            return Result.Failure<Unit>(AccountErrors.DestinationNotFound);
        }

        string actorId = _currentUser.UserId!;
        if (await _savingsAccountRepository.CountActiveByOwnerAsync(actorId, cancellationToken) < 2) {
            return Result.Failure<Unit>(
                DomainError.Validation(
                    "Account.MinimumActiveAccounts",
                    "El cliente debe tener al menos dos cuentas activas para transferir entre cuentas propias."
                )
            );
        }

        if (source.OwnerUserId != actorId || destination.OwnerUserId != actorId) {
            throw new ForbiddenAccessException("Ambas cuentas deben pertenecer al cliente autenticado.");
        }

        Result<FinancialOperationOutcome> outcomeResult = await _processor.TransferAsync(
            source,
            destination,
            amountResult.Value,
            TransferFlow.OwnAccounts,
            actorId,
            cancellationToken
        );
        if (outcomeResult.IsFailure) {
            return Result.Failure<Unit>(outcomeResult.Error!);
        }

        FinancialOperationOutcome outcome = outcomeResult.Value;
        try {
            var user = await _userRepository.GetByIdAsync(actorId, CancellationToken.None);
            if (user is not null) {
                await _emailService.SendAsync(
                    user.Email,
                    new TransferCompletedModel(
                        $"{user.FirstName} {user.LastName}".Trim(),
                        outcome.AppliedAmount,
                        source.Number.Value[^4..],
                        destination.Number.Value[^4..],
                        outcome.OccurredAt,
                        _clock.BusinessTimeZone
                    ),
                    CancellationToken.None
                );
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo de la transferencia propia {OperationId}.",
                outcome.OperationId
            );
        }

        return Result.Success(Unit.Value);
    }
}
