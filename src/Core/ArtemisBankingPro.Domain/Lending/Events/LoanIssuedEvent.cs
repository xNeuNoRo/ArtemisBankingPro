using ArtemisBankingPro.Domain.Common.Events;
using ArtemisBankingPro.Domain.Lending.ValueObjects;

namespace ArtemisBankingPro.Domain.Lending.Events;

/// <summary>
/// Se desencadena cuando un préstamo se emite y desembolsa. Lleva
/// los valores del correo "Préstamo aprobado": el Id de la entidad aún no
/// existe, por eso el payload transporta los datos necesarios.
/// </summary>
public sealed record LoanIssuedEvent(
    string CustomerUserId,
    LoanNumber LoanNumber,
    decimal ApprovedPrincipal,
    int TermMonths,
    decimal AnnualInterestRate,
    decimal MonthlyPayment
) : IDomainEvent;
