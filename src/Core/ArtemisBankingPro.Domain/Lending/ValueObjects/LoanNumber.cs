using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Errors;

namespace ArtemisBankingPro.Domain.Lending.ValueObjects;

/// <summary>
/// Representa un número de préstamo único, compuesto por 9 dígitos. Proporciona validación y creación segura del valor.
/// </summary>
public sealed record LoanNumber {
    private LoanNumber(string value) {
        Value = value;
    }

    public string Value { get; }

    public static Result<LoanNumber> Create(string? value) {
        if (value is null || value.Length != 9 || !value.All(char.IsAsciiDigit)) {
            return Result.Failure<LoanNumber>(LoanErrors.InvalidNumber);
        }

        return Result.Success(new LoanNumber(value));
    }

    public override string ToString() => Value;
}
