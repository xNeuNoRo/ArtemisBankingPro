using ArtemisBankingPro.Application.Interfaces.Identity;

namespace ArtemisBankingPro.Infrastructure.Identity.Entities;

/// <summary>
/// Token de activación o restablecimiento de contraseña. Solo se persiste el
/// hash HMAC-SHA256 del token crudo (con pepper); el token crudo viaja en el
/// correo y nunca se almacena. Un solo uso, con vencimiento, vinculado a
/// usuario y propósito.
/// </summary>
public sealed class AccountToken {
    private AccountToken() { }

    internal AccountToken(
        string userId,
        AccountTokenType type,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc
    ) {
        UserId = userId;
        Type = type;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    public long Id { get; private set; }

    public string UserId { get; private set; } = null!;

    public AccountTokenType Type { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? UsedAtUtc { get; private set; }

    public bool IsUsed => UsedAtUtc is not null;

    public bool IsExpired(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;

    public bool IsValid(DateTimeOffset nowUtc) => !IsUsed && !IsExpired(nowUtc);

    internal void MarkUsed(DateTimeOffset usedAtUtc) => UsedAtUtc = usedAtUtc;
}
