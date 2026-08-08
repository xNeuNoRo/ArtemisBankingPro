using ArtemisBankingPro.Application.Features.Loans.Commands;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using Mediator;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Loans.Handlers;

/// <summary>
/// Modifica la tasa de interés anual de un préstamo activo. El recalculo de
/// cuotas futuras pendientes lo aplica el dominio
/// (<see cref="Domain.Lending.Entities.Loan.ChangeInterestRate"/>).
/// </summary>
public sealed class UpdateLoanRateCommandHandler
    : IRequestHandler<UpdateLoanRateCommand, Result<Unit>> {
    private readonly ILoanRepository _loanRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;
    private readonly IEmailService _emailService;
    private readonly ILogger<UpdateLoanRateCommandHandler> _logger;

    public UpdateLoanRateCommandHandler(
        ILoanRepository loanRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        IEmailService emailService,
        ILogger<UpdateLoanRateCommandHandler> logger
    ) {
        _loanRepository = loanRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _emailService = emailService;
        _logger = logger;
    }

    public async ValueTask<Result<Unit>> Handle(
        UpdateLoanRateCommand message,
        CancellationToken cancellationToken
    ) {
        // 1. El préstamo debe existir con sus cuotas.
        var loan = await _loanRepository.GetWithInstallmentsByIdAsync(
            message.LoanId,
            cancellationToken
        );
        if (loan is null) {
            return Result.Failure<Unit>(
                DomainError.NotFound(
                    "Loan.NotFound",
                    "El préstamo seleccionado no existe."
                )
            );
        }

        // 2. Nueva tasa válida.
        var rateResult = InterestRate.Create(message.AnnualInterestRate);
        if (rateResult.IsFailure) {
            return Result.Failure<Unit>(rateResult.Error!);
        }

        // 3. Aplicar el cambio (dominio valida estado y cuotas elegibles).
        var changeResult = loan.ChangeInterestRate(rateResult.Value, _clock.Today);
        if (changeResult.IsFailure) {
            return Result.Failure<Unit>(changeResult.Error!);
        }

        // 4. Persistir.
        var persistResult = await _unitOfWork.ExecuteInTransactionAsync(
            _ => {
                _loanRepository.Update(loan);
                return Task.FromResult(Result.Success());
            },
            ct: cancellationToken
        );
        if (persistResult.IsFailure) {
            return Result.Failure<Unit>(persistResult.Error!);
        }

        // 5. Correo post-commit (fallo no revierte el cambio).
        await SendRateChangedEmailAsync(loan, cancellationToken);

        return Result.Success(Unit.Value);
    }

    private async Task SendRateChangedEmailAsync(
        Domain.Lending.Entities.Loan loan,
        CancellationToken cancellationToken
    ) {
        var customer = await _userRepository.GetByIdAsync(
            loan.CustomerUserId,
            cancellationToken
        );
        if (customer is null) {
            return;
        }

        var nextInstallment = loan.Installments
            .OrderBy(i => i.Number)
            .FirstOrDefault(i => i.Status == Domain.Lending.Enums.InstallmentStatus.Pending);

        try {
            await _emailService.SendAsync(
                customer.Email,
                new LoanRateChangedModel(
                    $"{customer.FirstName} {customer.LastName}".Trim(),
                    loan.Number.Value,
                    loan.AnnualInterestRate.AnnualPercentage,
                    nextInstallment?.ScheduledAmount ?? Money.Zero,
                    nextInstallment?.DueDate ?? default
                ),
                cancellationToken
            );
        }
        catch (EmailSendException ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo de cambio de tasa para el préstamo {LoanNumber}.",
                loan.Number.Value
            );
        }
    }
}
