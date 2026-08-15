using ArtemisBankingPro.Application.Features.Overdue.Commands;
using ArtemisBankingPro.Application.Features.Overdue.DTOs;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.Enums;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Overdue.Handlers;

public sealed class ProcessOverdueLoansCommandHandler
    : IRequestHandler<ProcessOverdueLoansCommand, Result<OverdueProcessingResult>> {
    private readonly ILoanRepository _loanRepository;
    private readonly IUserRepository _userRepository;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessOverdueLoansCommandHandler> _logger;

    public ProcessOverdueLoansCommandHandler(
        ILoanRepository loanRepository,
        IUserRepository userRepository,
        IServiceScopeFactory scopeFactory,
        IEmailService emailService,
        ILogger<ProcessOverdueLoansCommandHandler> logger
    ) {
        _loanRepository = loanRepository;
        _userRepository = userRepository;
        _scopeFactory = scopeFactory;
        _emailService = emailService;
        _logger = logger;
    }

    public async ValueTask<Result<OverdueProcessingResult>> Handle(
        ProcessOverdueLoansCommand message,
        CancellationToken cancellationToken
    ) {
        int totalProcessed = 0;
        int newDelinquent = 0;
        Money totalDelinquentAmount = Money.Zero;
        int afterLoanId = 0;

        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<int> loanIds =
                await _loanRepository.GetActivePastDueLoanIdsAsync(
                    message.BusinessDate,
                    afterLoanId,
                    message.BatchSize,
                    cancellationToken
                );
            if (loanIds.Count == 0) {
                break;
            }

            foreach (int loanId in loanIds) {
                cancellationToken.ThrowIfCancellationRequested();
                Loan? processedLoan = null;
                bool becameDelinquent = false;
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                ILoanRepository scopedLoanRepository =
                    scope.ServiceProvider.GetRequiredService<ILoanRepository>();
                IUnitOfWork scopedUnitOfWork =
                    scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                Result processResult = await scopedUnitOfWork.ExecuteInTransactionAsync(
                    async ct => {
                        Loan? loan = await scopedLoanRepository.GetWithInstallmentsByIdAsync(
                            loanId,
                            ct
                        );
                        if (loan is null || loan.Status != LoanStatus.Active) {
                            return Result.Success();
                        }

                        bool wasDelinquent = loan.IsDelinquent;
                        loan.RefreshDelinquency(message.BusinessDate);
                        becameDelinquent = !wasDelinquent && loan.IsDelinquent;
                        processedLoan = loan;
                        return Result.Success();
                    },
                    ct: cancellationToken
                );

                if (processResult.IsFailure) {
                    _logger.LogWarning(
                        "No se pudo actualizar la mora del préstamo {LoanId}: {ErrorCode}.",
                        loanId,
                        processResult.Error!.Code
                    );
                    continue;
                }

                if (processedLoan is null) {
                    continue;
                }

                totalProcessed++;
                if (processedLoan.IsDelinquent) {
                    foreach (
                        Installment installment in processedLoan.Installments.Where(item =>
                            item.IsOverdue
                        )
                    ) {
                        totalDelinquentAmount = totalDelinquentAmount.Add(
                            installment.RemainingAmount
                        );
                    }
                }

                if (becameDelinquent) {
                    newDelinquent++;
                    await TrySendNotificationAsync(
                        processedLoan,
                        message.BusinessDate,
                        cancellationToken
                    );
                }
            }

            afterLoanId = loanIds[^1];
            if (loanIds.Count < message.BatchSize) {
                break;
            }
        }

        _logger.LogInformation(
            "Procesamiento de mora completado para {BusinessDate}: {TotalProcessed} préstamos, {NewDelinquent} nuevos morosos, monto moroso {TotalDelinquentAmount}.",
            message.BusinessDate,
            totalProcessed,
            newDelinquent,
            totalDelinquentAmount.Amount
        );

        return Result.Success(
            new OverdueProcessingResult(
                totalProcessed,
                newDelinquent,
                totalDelinquentAmount.Amount
            )
        );
    }

    private async Task TrySendNotificationAsync(
        Loan loan,
        DateOnly businessDate,
        CancellationToken cancellationToken
    ) {
        if (!_emailService.IsConfigured || cancellationToken.IsCancellationRequested) {
            return;
        }

        try {
            UserListDto? customer = await _userRepository.GetByIdAsync(
                loan.CustomerUserId,
                CancellationToken.None
            );
            if (customer is null) {
                return;
            }

            await _emailService.SendAsync(
                customer.Email,
                new LoanDelinquentModel(
                    $"{customer.FirstName} {customer.LastName}".Trim(),
                    loan.Number.Value,
                    loan.OutstandingAmount,
                    businessDate
                ),
                CancellationToken.None
            );
        }
        catch (Exception ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar la notificación de mora del préstamo {LoanNumber}.",
                loan.Number.Value
            );
        }
    }
}
