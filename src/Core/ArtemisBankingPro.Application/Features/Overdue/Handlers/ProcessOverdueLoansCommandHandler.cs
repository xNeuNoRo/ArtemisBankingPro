using ArtemisBankingPro.Application.Features.Overdue.Commands;
using ArtemisBankingPro.Application.Features.Overdue.DTOs;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.Enums;
using Mediator;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Overdue.Handlers;

/// <summary>
/// Recalcula la mora de los préstamos activos por lotes acotados y
/// secuenciales, con un máximo de 1,000 préstamos por ejecución (cada préstamo
/// en su propia transacción, para que un fallo no revierta el trabajo ya
/// confirmado).
/// </summary>
/// <remarks>
/// Sin service locator: las dependencias se inyectan y cada
/// <see cref="IUnitOfWork.ExecuteInTransactionAsync"/> es una transacción
/// aislada (el UnitOfWork revierte y limpia el estado ante fallos, ADR-002).
/// Los fallos individuales no se silencian: se registran con su contexto y se
/// devuelven en <see cref="OverdueProcessingResult.FailedCount"/> para que el
/// host decida reintentar. Los fallos de notificación se separan en
/// <see cref="OverdueProcessingResult.EmailFailedCount"/> porque no deben
/// revertir ni volver a ejecutar el cambio de mora. La cancelación se propaga
/// a consultas y correos.
/// </remarks>
/// <summary>
/// Resultado de la transacción por préstamo: el préstamo recalculado y si
/// pasó a moroso en esta ejecución (para notificar solo la transición).
/// </summary>
public sealed record ProcessedLoanOutcome(Loan Loan, bool BecameDelinquent);

public sealed class ProcessOverdueLoansCommandHandler
    : IRequestHandler<ProcessOverdueLoansCommand, Result<OverdueProcessingResult>> {
    private const int MaxLoansPerRun = 1_000;

    private readonly ILoanRepository _loanRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessOverdueLoansCommandHandler> _logger;

    public ProcessOverdueLoansCommandHandler(
        ILoanRepository loanRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IEmailService emailService,
        ILogger<ProcessOverdueLoansCommandHandler> logger
    ) {
        _loanRepository = loanRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _emailService = emailService;
        _logger = logger;
    }

    public async ValueTask<Result<OverdueProcessingResult>> Handle(
        ProcessOverdueLoansCommand message,
        CancellationToken cancellationToken
    ) {
        int totalProcessed = 0;
        int newDelinquent = 0;
        int failedCount = 0;
        int emailFailedCount = 0;
        Money totalDelinquentAmount = Money.Zero;
        int afterLoanId = 0;
        int attemptedLoans = 0;
        bool hasMore = false;

        while (attemptedLoans < MaxLoansPerRun) {
            cancellationToken.ThrowIfCancellationRequested();
            int queryBatchSize = Math.Min(message.BatchSize, MaxLoansPerRun - attemptedLoans);
            IReadOnlyList<int> loanIds =
                await _loanRepository.GetActivePastDueLoanIdsAsync(
                    message.BusinessDate,
                    afterLoanId,
                    queryBatchSize,
                    cancellationToken
                );
            if (loanIds.Count == 0) {
                break;
            }

            foreach (int loanId in loanIds) {
                cancellationToken.ThrowIfCancellationRequested();
                attemptedLoans++;
                Result<ProcessedLoanOutcome> processResult =
                    await _unitOfWork.ExecuteInTransactionAsync<ProcessedLoanOutcome>(
                        async ct => {
                            Loan? loan = await _loanRepository.GetWithInstallmentsByIdAsync(
                                loanId,
                                ct
                            );
                            if (loan is null || loan.Status != LoanStatus.Active) {
                                return Result.Success<ProcessedLoanOutcome>(null!);
                            }

                            bool wasDelinquent = loan.IsDelinquent;
                            loan.RefreshDelinquency(message.BusinessDate);
                            return Result.Success(
                                new ProcessedLoanOutcome(loan, !wasDelinquent && loan.IsDelinquent)
                            );
                        },
                        ct: cancellationToken
                    );

                if (processResult.IsFailure) {
                    failedCount++;
                    _logger.LogWarning(
                        "No se pudo actualizar la mora del préstamo {LoanId} en {BusinessDate}: {ErrorCode}.",
                        loanId,
                        message.BusinessDate,
                        processResult.Error!.Code
                    );
                    continue;
                }

                ProcessedLoanOutcome? info = processResult.Value;
                if (info is null) {
                    continue;
                }

                totalProcessed++;
                if (info.Loan.IsDelinquent) {
                    foreach (
                        Installment installment in info.Loan.Installments.Where(item =>
                            item.IsOverdue
                        )
                    ) {
                        totalDelinquentAmount = totalDelinquentAmount.Add(
                            installment.RemainingAmount
                        );
                    }
                }

                if (info.BecameDelinquent) {
                    newDelinquent++;
                    if (_emailService.IsConfigured) {
                        bool sent = await TrySendNotificationAsync(
                            info.Loan,
                            message.BusinessDate,
                            cancellationToken
                        );
                        if (!sent) {
                            emailFailedCount++;
                        }
                    }
                }
            }

            afterLoanId = loanIds[^1];
            if (loanIds.Count < queryBatchSize) {
                break;
            }

            if (attemptedLoans == MaxLoansPerRun) {
                IReadOnlyList<int> remainingLoanIds =
                    await _loanRepository.GetActivePastDueLoanIdsAsync(
                        message.BusinessDate,
                        afterLoanId,
                        1,
                        cancellationToken
                    );
                hasMore = remainingLoanIds.Count > 0;
                break;
            }
        }

        _logger.LogInformation(
            "Procesamiento de mora completado para {BusinessDate}: {TotalProcessed} préstamos, {NewDelinquent} nuevos morosos, {FailedCount} fallos de procesamiento, {EmailFailedCount} fallos de correo, monto moroso {TotalDelinquentAmount}.",
            message.BusinessDate,
            totalProcessed,
            newDelinquent,
            failedCount,
            emailFailedCount,
            totalDelinquentAmount.Amount
        );

        return Result.Success(
            new OverdueProcessingResult(
                totalProcessed,
                newDelinquent,
                totalDelinquentAmount.Amount,
                failedCount,
                emailFailedCount,
                hasMore
            )
        );
    }

    private async Task<bool> TrySendNotificationAsync(
        Loan loan,
        DateOnly businessDate,
        CancellationToken cancellationToken
    ) {
        try {
            UserListDto? customer = await _userRepository.GetByIdAsync(
                loan.CustomerUserId,
                cancellationToken
            );
            if (customer is null) {
                _logger.LogWarning(
                        "No se encontró el cliente para notificación de mora del préstamo terminado en {LoanLastFour}.",
                        loan.Number.Value[^4..]
                );
                return false;
            }

            await _emailService.SendAsync(
                customer.Email,
                new LoanDelinquentModel(
                    $"{customer.FirstName} {customer.LastName}".Trim(),
                    loan.Number.Value,
                    loan.OutstandingAmount,
                    businessDate
                ),
                cancellationToken
            );
            return true;
        }
        catch (EmailSendException ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar la notificación de mora del préstamo terminado en {LoanLastFour}; tipo de error {ExceptionType}.",
                loan.Number.Value[^4..],
                ex.InnerException?.GetType().Name ?? ex.GetType().Name
            );
            return false;
        }
    }
}
