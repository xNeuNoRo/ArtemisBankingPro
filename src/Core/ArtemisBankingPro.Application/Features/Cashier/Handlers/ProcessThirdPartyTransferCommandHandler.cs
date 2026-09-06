using ArtemisBankingPro.Application.Common;
using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
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

namespace ArtemisBankingPro.Application.Features.Cashier.Handlers;

/// <summary>
/// Procesa una transferencia a cuentas de terceros (spec §31): valida origen
/// y destino y la regla de terceros, delega el núcleo financiero atómico en
/// <see cref="ITransferProcessor"/> y envía los correos a ambos propietarios
/// después del commit (su fallo no revierte la transferencia).
/// </summary>
public sealed class ProcessThirdPartyTransferCommandHandler
    : IRequestHandler<ProcessThirdPartyTransferCommand, Result<ProcessThirdPartyTransferResponse>> {
    private readonly ITransferProcessor _processor;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessThirdPartyTransferCommandHandler> _logger;

    public ProcessThirdPartyTransferCommandHandler(
        ITransferProcessor processor,
        ISavingsAccountRepository savingsAccountRepository,
        IUserRepository userRepository,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessThirdPartyTransferCommandHandler> logger
    ) {
        _processor = processor;
        _savingsAccountRepository = savingsAccountRepository;
        _userRepository = userRepository;
        _clock = clock;
        _currentUser = currentUser;
        _emailService = emailService;
        _logger = logger;
    }

    public async ValueTask<Result<ProcessThirdPartyTransferResponse>> Handle(
        ProcessThirdPartyTransferCommand message,
        CancellationToken cancellationToken
    ) {
        var sourceNumberResult = AccountNumber.Create(message.SourceAccountNumber);
        if (sourceNumberResult.IsFailure) {
            return Result.Failure<ProcessThirdPartyTransferResponse>(sourceNumberResult.Error!);
        }

        var destinationNumberResult = AccountNumber.Create(message.DestinationAccountNumber);
        if (destinationNumberResult.IsFailure) {
            return Result.Failure<ProcessThirdPartyTransferResponse>(
                destinationNumberResult.Error!
            );
        }

        if (sourceNumberResult.Value == destinationNumberResult.Value) {
            return Result.Failure<ProcessThirdPartyTransferResponse>(OperationErrors.SameAccount);
        }

        var amountResult = Money.Create(message.Amount);
        if (amountResult.IsFailure) {
            return Result.Failure<ProcessThirdPartyTransferResponse>(amountResult.Error!);
        }

        var source = await _savingsAccountRepository.GetByNumberAsync(
            sourceNumberResult.Value,
            cancellationToken
        );
        if (source is null) {
            return Result.Failure<ProcessThirdPartyTransferResponse>(AccountErrors.SourceNotFound);
        }

        var destination = await _savingsAccountRepository.GetByNumberAsync(
            destinationNumberResult.Value,
            cancellationToken
        );
        if (destination is null) {
            return Result.Failure<ProcessThirdPartyTransferResponse>(
                AccountErrors.DestinationNotFound
            );
        }

        if (destination.OwnerUserId == source.OwnerUserId) {
            return Result.Failure<ProcessThirdPartyTransferResponse>(
                OperationErrors.DestinationMustBeThirdParty
            );
        }

        var outcomeResult = await _processor.TransferAsync(
            source,
            destination,
            amountResult.Value,
            TransferFlow.CashierThirdParty,
            _currentUser.UserId!,
            cancellationToken
        );
        if (outcomeResult.IsFailure) {
            return Result.Failure<ProcessThirdPartyTransferResponse>(outcomeResult.Error!);
        }

        var outcome = outcomeResult.Value;
        bool notificationsOk = await SendNotificationsAsync(
            outcome.OperationId,
            source,
            destination,
            outcome.AppliedAmount,
            outcome.OccurredAt,
            CancellationToken.None
        );

        return Result.Success(
            new ProcessThirdPartyTransferResponse(
                outcome.OperationId,
                source.Number.Value,
                destination.Number.Value,
                outcome.AppliedAmount.Amount,
                outcome.OccurredAt,
                "Approved",
                NotificationWarning: notificationsOk ? null : NotificationMessages.EmailFailed
            )
        );
    }

    private async Task<bool> SendNotificationsAsync(
        Guid operationId,
        SavingsAccount source,
        SavingsAccount destination,
        Money amount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    ) {
        IReadOnlyList<UserListDto> owners = await _userRepository.GetByIdsAsync(
            [source.OwnerUserId, destination.OwnerUserId],
            cancellationToken
        );

        UserListDto? sourceOwner = owners.FirstOrDefault(owner => owner.Id == source.OwnerUserId);
        UserListDto? destinationOwner = owners.FirstOrDefault(
            owner => owner.Id == destination.OwnerUserId
        );

        bool allSent = await TrySendAsync(
            sourceOwner,
            new ThirdPartyTransferSenderModel(
                FullName(sourceOwner),
                amount,
                source.Number.Value[^4..],
                destination.Number.Value[^4..],
                occurredAt,
                _clock.BusinessTimeZone
            ),
            operationId,
            cancellationToken
        );

        return (await TrySendAsync(
            destinationOwner,
            new ThirdPartyTransferReceiverModel(
                FullName(destinationOwner),
                amount,
                source.Number.Value[^4..],
                destination.Number.Value[^4..],
                occurredAt,
                _clock.BusinessTimeZone
            ),
            operationId,
            cancellationToken
        )) && allSent;
    }

    private async Task<bool> TrySendAsync<T>(
        UserListDto? recipient,
        T model,
        Guid operationId,
        CancellationToken cancellationToken
    )
        where T : IEmailModel {
        if (recipient is null) {
            return true;
        }

        try {
            await _emailService.SendAsync(recipient.Email, model, cancellationToken);
            return true;
        }
        catch (EmailSendException ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo {Template} tras la transferencia a terceros {OperationId}.",
                model.TemplateName,
                operationId
            );
            return false;
        }
    }

    private static string FullName(UserListDto? user) =>
        user is null ? string.Empty : $"{user.FirstName} {user.LastName}".Trim();
}
