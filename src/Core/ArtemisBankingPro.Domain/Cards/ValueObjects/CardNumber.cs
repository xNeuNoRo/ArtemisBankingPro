using ArtemisBankingPro.Domain.Cards.Errors;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Cards.ValueObjects;

public sealed record CardNumber {
    private CardNumber(string value) {
        Value = value;
    }

    private string Value { get; }

    public string LastFour => Value[^4..];

    public string Masked => $"************{LastFour}";

    public static Result<CardNumber> Create(string? value) {
        if (value is null || value.Length != 16 || !value.All(char.IsAsciiDigit)) {
            return Result.Failure<CardNumber>(CardErrors.InvalidNumber);
        }

        return Result.Success(new CardNumber(value));
    }

    public override string ToString() => Masked;
}
