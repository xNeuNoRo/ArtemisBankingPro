using ArtemisBankingPro.Domain.Cards.Errors;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Cards.ValueObjects;

public sealed record CvcDigest {
    private CvcDigest(string value) {
        Value = value;
    }

    private string Value { get; }

    public static Result<CvcDigest> Create(string? value) {
        if (!IsSha256Digest(value)) {
            return Result.Failure<CvcDigest>(CardErrors.InvalidCvcDigest);
        }

        return Result.Success(new CvcDigest(value!));
    }

    internal string GetValue() => Value;

    public override string ToString() => "[PROTECTED]";

    private static bool IsSha256Digest(string? value) {
        if (value is null) {
            return false;
        }

        if (value.Length == 64 && value.All(Uri.IsHexDigit)) {
            return true;
        }

        Span<byte> bytes = stackalloc byte[32];
        return Convert.TryFromBase64String(value, bytes, out int bytesWritten)
            && bytesWritten == 32;
    }
}
