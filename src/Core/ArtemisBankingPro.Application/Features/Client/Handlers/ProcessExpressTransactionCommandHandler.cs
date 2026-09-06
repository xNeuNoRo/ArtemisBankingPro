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

public sealed class ProcessExpressTransactionCommandHandler
    : IRequestHandler<ProcessExpressTransactionCommand, Result<Unit>> {
    private readonly ITransferProcessor _processor;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessExpressTransactionCommandHandler> _logger;

    public ProcessExpressTransactionCommandHandler(
        ITransferProcessor processor,
        ISavingsAccountRepository savingsAccountRepository,
        IUserRepository userRepository,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessExpressTransactionCommandHandler> logger
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
        ProcessExpressTransactionCommand message,
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
        if (source is null) {
            return Result.Failure<Unit>(AccountErrors.SourceNotFound);
        }

        string actorId = _currentUser.UserId!;
        if (source.OwnerUserId != actorId) {
            throw new ForbiddenAccessException("La cuenta origen no pertenece al cliente autenticado.");
        }

        SavingsAccount? destination = await _savingsAccountRepository.GetByNumberAsync(
            destinationNumber.Value,
            cancellationToken
        );
        if (destination is null) {
            return Result.Failure<Unit>(AccountErrors.DestinationNotFound);
        }

        if (destination.OwnerUserId == source.OwnerUserId) {
            return Result.Failure<Unit>(OperationErrors.DestinationMustBeThirdParty);
        }

        Result<FinancialOperationOutcome> outcomeResult = await _processor.TransferAsync(
            source,
            destination,
            amountResult.Value,
            TransferFlow.Express,
            actorId,
            cancellationToken
        );
        if (outcomeResult.IsFailure) {
            return Result.Failure<Unit>(outcomeResult.Error!);
        }

        FinancialOperationOutcome outcome = outcomeResult.Value;
        try {
            await SendNotificationsAsync(
                source,
                destination,
                outcome.AppliedAmount,
                outcome.OccurredAt,
                outcome.OperationId,
                CancellationToken.None
            );
        }
        catch (Exception ex) {
            _logger.LogWarning(
                ex,
                "No se pudieron completar las notificaciones de la transferencia express {OperationId}.",
                outcome.OperationId
            );
        }

        return Result.Success(Unit.Value);
    }

    private async Task SendNotificationsAsync(
        SavingsAccount source,
        SavingsAccount destination,
        Money amount,
        DateTimeOffset occurredAt,
        Guid operationId,
        CancellationToken cancellationToken
    ) {
        var users = await _userRepository.GetByIdsAsync(
            [source.OwnerUserId, destination.OwnerUserId],
            cancellationToken
        );
        var sender = users.FirstOrDefault(user => user.Id == source.OwnerUserId);
        var receiver = users.FirstOrDefault(user => user.Id == destination.OwnerUserId);

        if (sender is not null) {
            await TrySendAsync(
                sender,
                new ThirdPartyTransferSenderModel(
                    $"{sender.FirstName} {sender.LastName}".Trim(),
                    amount,
                    source.Number.Value[^4..],
                    destination.Number.Value[^4..],
                    occurredAt,
                    _clock.BusinessTimeZone
                ),
                operationId,
                cancellationToken
            );
        }

        if (receiver is not null) {
            await TrySendAsync(
                receiver,
                new ThirdPartyTransferReceiverModel(
                    $"{receiver.FirstName} {receiver.LastName}".Trim(),
                    amount,
                    source.Number.Value[^4..],
                    destination.Number.Value[^4..],
                    occurredAt,
                    _clock.BusinessTimeZone
                ),
                operationId,
                cancellationToken
            );
        }
    }

    private async Task TrySendAsync<T>(
        UserListDto recipient,
        T model,
        Guid operationId,
        CancellationToken cancellationToken
    )
        where T : IEmailModel {
        try {
            await _emailService.SendAsync(recipient.Email, model, cancellationToken);
        }
        catch (EmailSendException ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo {Template} de la transferencia express {OperationId}.",
                model.TemplateName,
                operationId
            );
        }
    }
}
