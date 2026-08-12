using ArtemisBankingPro.Application.Features.HermesPay.Commands;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.HermesPay.Validators;

/// <summary>
/// Valida la forma del request de procesamiento de pago Hermes Pay (spec §41):
/// número de tarjeta de 16 dígitos, mes MM, año YYYY, CVC de 3 dígitos y monto
/// positivo. Las invariantes mutables (comercio, tarjeta, crédito disponible)
/// se revalidan en el handler dentro de la transacción.
/// </summary>
public sealed class ProcessHermesPayCommandValidator
    : AbstractValidator<ProcessHermesPayCommand> {
    public ProcessHermesPayCommandValidator() {
        RuleFor(x => x.CardNumber)
            .NotEmpty()
            .WithMessage("El número de tarjeta es requerido.")
            .Matches("^[0-9]{16}$")
            .WithMessage("El número de tarjeta debe contener exactamente 16 dígitos.");

        RuleFor(x => x.MonthExpirationCard)
            .NotEmpty()
            .WithMessage("El mes de expiración es requerido.")
            .Matches("^(0[1-9]|1[0-2])$")
            .WithMessage("El mes de expiración debe estar entre 01 y 12.");

        RuleFor(x => x.YearExpirationCard)
            .NotEmpty()
            .WithMessage("El año de expiración es requerido.")
            .Matches("^[0-9]{4}$")
            .WithMessage("El año de expiración debe tener formato YYYY.");

        RuleFor(x => x.Cvc)
            .NotEmpty()
            .WithMessage("El CVC es requerido.")
            .Matches("^[0-9]{3}$")
            .WithMessage("El CVC debe contener exactamente 3 dígitos.");

        RuleFor(x => x.TransactionAmount)
            .GreaterThan(0m)
            .WithMessage("El monto de la transacción debe ser mayor que cero.");
    }
}
