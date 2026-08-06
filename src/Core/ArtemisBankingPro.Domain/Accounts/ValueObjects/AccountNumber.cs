using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Accounts.ValueObjects;

public sealed record AccountNumber {
    private AccountNumber(string value) {
        Value = value;
    }

    public string Value { get; }

    public static Result<AccountNumber> Create(string? value) {
        if (value is null || value.Length != 9 || !value.All(char.IsAsciiDigit)) {
            return Result.Failure<AccountNumber>(AccountErrors.InvalidNumber);
        }

        return Result.Success(new AccountNumber(value));
    }

    public override string ToString() => Value;
}
