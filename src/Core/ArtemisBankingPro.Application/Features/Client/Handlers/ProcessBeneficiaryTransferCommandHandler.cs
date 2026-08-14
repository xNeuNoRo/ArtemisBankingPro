using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

public sealed class ProcessBeneficiaryTransferCommandHandler
    : IRequestHandler<ProcessBeneficiaryTransferCommand, Result<Unit>> {
    private static readonly DomainError BeneficiaryNotFound = DomainError.NotFound(
        "Beneficiary.NotFound",
        "El beneficiario indicado no existe."
    );

    private readonly IBeneficiaryRepository _beneficiaryRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessBeneficiaryTransferCommandHandler> _logger;

    public ProcessBeneficiaryTransferCommandHandler(
        IBeneficiaryRepository beneficiaryRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessBeneficiaryTransferCommandHandler> logger
    ) {
        _beneficiaryRepository = beneficiaryRepository;
        _savingsAccountRepository = savingsAccountRepository;
        _financialOperationRepository = financialOperationRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
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
        Result<Money> amountResult = Money.Create(message.Amount);
        if (sourceNumber.IsFailure) {
            return Result.Failure<Unit>(sourceNumber.Error!);
        }

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

        if (source.Status != AccountStatus.Active || destination.Status != AccountStatus.Active) {
            return Result.Failure<Unit>(AccountErrors.NotActive);
        }

        Money amount = amountResult.Value;
        Result canDebit = source.CanDebit(amount);
        if (canDebit.IsFailure) {
            Result rejection = await PersistRejectionAsync(
                source,
                destination,
                amount,
                canDebit.Error!,
                cancellationToken
            );
            if (rejection.IsFailure) {
                return Result.Failure<Unit>(rejection.Error!);
            }

            return Result.Failure<Unit>(canDebit.Error!);
        }

        Result canCredit = destination.CanCredit(amount);
        if (canCredit.IsFailure) {
            return Result.Failure<Unit>(canCredit.Error!);
        }

        Guid operationId = Guid.NewGuid();
        DateTimeOffset occurredAt = _clock.Now;
        Result persistResult = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                Result<FinancialOperation> operation = FinancialOperation.Approve(
                    operationId,
                    FinancialOperationKind.BeneficiaryTransfer,
                    amount,
                    amount,
                    Money.Zero,
                    actorId,
                    occurredAt,
                    CreateTransactions(source, destination, amount)
                );
                if (operation.IsFailure) {
                    return Result.Failure(operation.Error!);
                }

                Result debit = source.Debit(amount);
                if (debit.IsFailure) {
                    return debit;
                }

                Result credit = destination.Credit(amount);
                if (credit.IsFailure) {
                    return credit;
                }

                foreach (SavingsAccount account in new[] { source, destination }.OrderBy(x => x.Id)) {
                    _savingsAccountRepository.Update(account);
                }

                await _financialOperationRepository.AddAsync(operation.Value, ct);
                return Result.Success();
            },
            ct: cancellationToken
        );
        if (persistResult.IsFailure) {
            return Result.Failure<Unit>(persistResult.Error!);
        }

        try {
            await SendNotificationsAsync(
                source,
                destination,
                amount,
                occurredAt,
                operationId,
                CancellationToken.None
            );
        }
        catch (Exception ex) {
            _logger.LogWarning(
                ex,
                "No se pudieron completar las notificaciones del beneficiario {OperationId}.",
                operationId
            );
        }

        return Result.Success(Unit.Value);
    }

    private async Task<Result> PersistRejectionAsync(
        SavingsAccount source,
        SavingsAccount destination,
        Money amount,
        DomainError error,
        CancellationToken cancellationToken
    ) {
        Result<FinancialOperation> operation = FinancialOperation.Reject(
            Guid.NewGuid(),
            FinancialOperationKind.BeneficiaryTransfer,
            amount,
            Money.Zero,
            _currentUser.UserId!,
            _clock.Now,
            error.Code,
            [
                new(source.Number, TransactionDirection.Debit, amount, source.Number.Value, destination.Number.Value),
            ]
        );
        if (operation.IsFailure) {
            return Result.Failure(operation.Error!);
        }

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                await _financialOperationRepository.AddAsync(operation.Value, ct);
                return Result.Success();
            },
            ct: cancellationToken
        );
    }

    private static IReadOnlyCollection<AccountTransactionDetails> CreateTransactions(
        SavingsAccount source,
        SavingsAccount destination,
        Money amount
    ) =>
        [
            new(source.Number, TransactionDirection.Debit, amount, source.Number.Value, destination.Number.Value),
            new(destination.Number, TransactionDirection.Credit, amount, source.Number.Value, destination.Number.Value),
        ];

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
