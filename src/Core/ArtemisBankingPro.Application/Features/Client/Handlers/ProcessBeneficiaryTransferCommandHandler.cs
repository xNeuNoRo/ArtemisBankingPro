using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Application.Features.FinancialProcessors;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

public sealed class ProcessBeneficiaryTransferCommandHandler
    : IRequestHandler<ProcessBeneficiaryTransferCommand, Result<Unit>> {
    private static readonly DomainError BeneficiaryNotFound = DomainError.NotFound(
        "Beneficiary.NotFound",
        "El beneficiario indicado no existe."
    );

    private readonly ITransferProcessor _processor;
    private readonly IBeneficiaryRepository _beneficiaryRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessBeneficiaryTransferCommandHandler> _logger;

    public ProcessBeneficiaryTransferCommandHandler(
        ITransferProcessor processor,
        IBeneficiaryRepository beneficiaryRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IUserRepository userRepository,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessBeneficiaryTransferCommandHandler> logger
    ) {
        _processor = processor;
        _beneficiaryRepository = beneficiaryRepository;
        _savingsAccountRepository = savingsAccountRepository;
        _userRepository = userRepository;
        _clock = clock;
        _currentUser = currentUser;
        _emailService = emailService;
        _logger = logger;
    }

    public async ValueTask<Result<Unit>> Handle(
        ProcessBeneficiaryTransferCommand message,
        CancellationToken cancellationToken
    ) {
        Result<AccountNumber> sourceNumber = AccountNumber.Create(message.SourceAccountNumber);
        if (sourceNumber.IsFailure) {
            return Result.Failure<Unit>(sourceNumber.Error!);
        }

        Result<Money> amountResult = Money.Create(message.Amount);
        if (amountResult.IsFailure) {
            return Result.Failure<Unit>(amountResult.Error!);
        }

        string actorId = _currentUser.UserId!;
        Beneficiary? beneficiary = await _beneficiaryRepository.GetByIdAsync(
            message.BeneficiaryId,
            cancellationToken
        );
        if (beneficiary is null) {
            return Result.Failure<Unit>(BeneficiaryNotFound);
        }

        if (beneficiary.OwnerUserId != actorId) {
            throw new ForbiddenAccessException("El beneficiario no pertenece al cliente autenticado.");
        }

        SavingsAccount? source = await _savingsAccountRepository.GetByNumberAsync(
            sourceNumber.Value,
            cancellationToken
        );
        SavingsAccount? destination = await _savingsAccountRepository.GetByIdAsync(
            beneficiary.DestinationAccountId,
            cancellationToken
        );
        if (source is null) {
            return Result.Failure<Unit>(AccountErrors.SourceNotFound);
        }

        if (source.OwnerUserId != actorId) {
            throw new ForbiddenAccessException("La cuenta origen no pertenece al cliente autenticado.");
        }

        if (destination is null) {
            return Result.Failure<Unit>(AccountErrors.DestinationNotFound);
        }

        Result<FinancialOperationOutcome> outcomeResult = await _processor.TransferAsync(
            source,
            destination,
            amountResult.Value,
            TransferFlow.Beneficiary,
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
                "No se pudieron completar las notificaciones del beneficiario {OperationId}.",
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
                "No se pudo enviar el correo {Template} de beneficiario {OperationId}.",
                model.TemplateName,
                operationId
            );
        }
    }
}
