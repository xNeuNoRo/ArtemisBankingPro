using ArtemisBankingPro.Domain.Lending.Errors;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Lending.ValueObjects;

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
