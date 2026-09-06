using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Details;

namespace ArtemisBankingPro.Domain.Lending.Policies;

/// <summary>
/// Evalúa el riesgo de un préstamo comparando la deuda actual y proyectada con la deuda promedio del cliente.
/// </summary>
public static class LoanRiskPolicy {
    public static LoanRiskAssessment Evaluate(
        Money currentDebt,
        Money newLoanTotalPayable,
        Money averageDebt
    ) {
        Money projectedDebt = currentDebt.Add(newLoanTotalPayable);
        return new LoanRiskAssessment(currentDebt, projectedDebt, averageDebt);
    }
}
