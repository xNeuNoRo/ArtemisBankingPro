using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Domain.Operations.Errors;
using Mediator;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Cashier.Handlers;

/// <summary>
/// Procesa una transferencia a cuentas de terceros (spec §31): valida origen
/// activo con fondos, destino activo de dueño distinto y ejecuta el débito,
/// el crédito, las dos transacciones pareadas y la operación financiera de
/// forma atómica. Los correos a ambos propietarios se envían después del
/// commit y su fallo no revierte la transferencia.
/// </summary>
public sealed class ProcessThirdPartyTransferCommandHandler
    : IRequestHandler<ProcessThirdPartyTransferCommand, Result<ProcessThirdPartyTransferResponse>> {
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessThirdPartyTransferCommandHandler> _logger;

    public ProcessThirdPartyTransferCommandHandler(
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessThirdPartyTransferCommandHandler> logger
    ) {
        _savingsAccountRepository = savingsAccountRepository;
        _financialOperationRepository = financialOperationRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _currentUser = currentUser;
        _emailService = emailService;
        _logger = logger;
    }

    public async ValueTask<Result<ProcessThirdPartyTransferResponse>> Handle(
        ProcessThirdPartyTransferCommand message,
        CancellationToken cancellationToken
    ) {
        // 1. Identificadores válidos y cuenta destino distinta de la origen.
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

        // 2. Monto válido.
        var amountResult = Money.Create(message.Amount);
        if (amountResult.IsFailure) {
            return Result.Failure<ProcessThirdPartyTransferResponse>(amountResult.Error!);
        }

        var amount = amountResult.Value;

        // 3. Origen: debe existir y estar activo.
        var source = await _savingsAccountRepository.GetByNumberAsync(
            sourceNumberResult.Value,
            cancellationToken
        );
        if (source is null) {
            return Result.Failure<ProcessThirdPartyTransferResponse>(AccountErrors.SourceNotFound);
        }

        if (source.Status != AccountStatus.Active) {
            return Result.Failure<ProcessThirdPartyTransferResponse>(AccountErrors.NotActive);
        }

        // 4. Fondos suficientes (se revalida bajo protección de concurrencia
        // dentro de la transacción).
        var canDebitResult = source.CanDebit(amount);
        if (canDebitResult.IsFailure) {
            return Result.Failure<ProcessThirdPartyTransferResponse>(canDebitResult.Error!);
        }

        // 5. Destino: debe existir, estar activo y pertenecer a un tercero.
        var destination = await _savingsAccountRepository.GetByNumberAsync(
            destinationNumberResult.Value,
            cancellationToken
        );
        if (destination is null) {
            return Result.Failure<ProcessThirdPartyTransferResponse>(
                AccountErrors.DestinationNotFound
            );
        }

        if (destination.Status != AccountStatus.Active) {
            return Result.Failure<ProcessThirdPartyTransferResponse>(AccountErrors.NotActive);
        }

        if (destination.OwnerUserId == source.OwnerUserId) {
            return Result.Failure<ProcessThirdPartyTransferResponse>(
                OperationErrors.DestinationMustBeThirdParty
            );
        }

        // 6. Débito + crédito + transacciones pareadas + operación, atómicos.
        var occurredAt = _clock.Now;
        Guid operationId = Guid.NewGuid();

        var persistResult = await ExecuteTransferAsync(
            operationId,
            source,
            destination,
            amount,
            occurredAt,
            cancellationToken
        );
        if (persistResult.IsFailure) {
            return Result.Failure<ProcessThirdPartyTransferResponse>(persistResult.Error!);
        }

        // 7. Correos post-commit a ambos propietarios (fallo no revierte).
        await SendNotificationsAsync(
            operationId,
            source,
            destination,
            amount,
            occurredAt,
            cancellationToken
        );

        return Result.Success(
            new ProcessThirdPartyTransferResponse(
                operationId,
                source.Number.Value,
                destination.Number.Value,
                amount.Amount,
                occurredAt,
                "Approved"
            )
        );
    }

    private async Task<Result> ExecuteTransferAsync(
        Guid operationId,
        SavingsAccount source,
        SavingsAccount destination,
        Money amount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    ) {
        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                var debitResult = source.Debit(amount);
                if (debitResult.IsFailure) {
                    return debitResult;
                }

                var creditResult = destination.Credit(amount);
                if (creditResult.IsFailure) {
                    return creditResult;
                }

                // Orden estable (Id ascendente) al actualizar las filas para
                // reducir deadlocks en operaciones concurrentes.
                foreach (SavingsAccount account in new[] { source, destination }.OrderBy(
                    account => account.Id
                )) {
                    _savingsAccountRepository.Update(account);
                }

                var operationResult = FinancialOperation.Approve(
                    operationId,
                    FinancialOperationKind.CashierTransfer,
                    amount,
                    amount,
                    Money.Zero,
                    _currentUser.UserId!,
                    occurredAt,
                    [
                        new AccountTransactionDetails(
                            source.Number,
                            TransactionDirection.Debit,
                            amount,
                            source.Number.Value,
                            destination.Number.Value
                        ),
                        new AccountTransactionDetails(
                            destination.Number,
                            TransactionDirection.Credit,
                            amount,
                            source.Number.Value,
                            destination.Number.Value
                        ),
                    ]
                );
                if (operationResult.IsFailure) {
                    return Result.Failure(operationResult.Error!);
                }

                var operation = operationResult.Value;
                operation.RecordThirdPartyTransferProcessed(
                    source.Number.Value,
                    destination.Number.Value,
                    amount,
                    source.OwnerUserId,
                    destination.OwnerUserId,
                    _currentUser.UserId!
                );

                await _financialOperationRepository.AddAsync(operation, ct);

                return Result.Success();
            },
            ct: cancellationToken
        );
    }

    private async Task SendNotificationsAsync(
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

        await TrySendAsync(
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

        await TrySendAsync(
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
        );
    }

    private async Task TrySendAsync<T>(
        UserListDto? recipient,
        T model,
        Guid operationId,
        CancellationToken cancellationToken
    )
        where T : IEmailModel {
        if (recipient is null) {
            return;
        }

        try {
            await _emailService.SendAsync(recipient.Email, model, cancellationToken);
        }
        catch (EmailSendException ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo {Template} tras la transferencia a terceros {OperationId}.",
                model.TemplateName,
                operationId
            );
        }
    }

    private static string FullName(UserListDto? user) =>
        user is null ? string.Empty : $"{user.FirstName} {user.LastName}".Trim();
}
