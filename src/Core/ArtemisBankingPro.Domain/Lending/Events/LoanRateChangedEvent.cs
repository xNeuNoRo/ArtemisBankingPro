using ArtemisBankingPro.Domain.Common.Events;
using ArtemisBankingPro.Domain.Lending.ValueObjects;

namespace ArtemisBankingPro.Domain.Lending.Events;

/// <summary>
/// Se desencadena cuando cambia la tasa de un préstamo activo. Incluye la nueva
/// tasa y el valor/fecha de la próxima cuota pendiente para el correo
/// "Actualización de tasa de interés de préstamo".
/// </summary>
public sealed record LoanRateChangedEvent(
    string CustomerUserId,
    LoanNumber LoanNumber,
    decimal NewAnnualInterestRate,
    decimal NextInstallmentAmount,
    DateOnly NextInstallmentDueDate
) : IDomainEvent;
