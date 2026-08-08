using System.Security.Cryptography;
using System.Text;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Identity.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArtemisBankingPro.Infrastructure.Identity.Services;

/// <summary>
/// Tokens de activación y restablecimiento: crudos (base64url de 32 bytes
/// aleatorios) entregados por correo, persistiendo solo su hash HMAC-SHA256
/// con pepper. Un solo uso, con vencimiento, vinculados a usuario y propósito.
/// </summary>
public sealed class AccountTokenService : IAccountTokenService {
    private readonly IdentityContext _context;
    private readonly AccountTokenOptions _options;
    private readonly TimeProvider _timeProvider;

    public AccountTokenService(
        IdentityContext context,
        IOptions<AccountTokenOptions> options,
        TimeProvider timeProvider
    ) {
        _context = context;
        _options = options.Value;
        _timeProvider = timeProvider;

        if (string.IsNullOrWhiteSpace(_options.PepperKey)) {
            throw new InvalidOperationException(
                "Security:AccountTokens:PepperKey no está configurada. "
                    + "Provea una clave HMAC de 32 bytes en base64."
            );
        }
    }

    public async Task<string> GenerateAsync(
        string userId,
        AccountTokenType type,
        CancellationToken ct = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        string rawToken = CreateRawToken();
        string tokenHash = ComputeHash(rawToken);

        // Invalida tokens previos no usados del mismo usuario
        List<AccountToken> previous = await _context
            .AccountTokens.Where(token =>
                token.UserId == userId && token.Type == type && token.UsedAtUtc == null
            )
            .ToListAsync(ct);

        foreach (AccountToken token in previous) {
            token.MarkUsed(nowUtc);
        }

        var created = new AccountToken(
            userId,
            type,
            tokenHash,
            nowUtc.AddMinutes(GetLifetimeMinutes(type)),
            nowUtc
        );

        _context.AccountTokens.Add(created);
        await _context.SaveChangesAsync(ct);

        return rawToken;
    }

    public async Task<AccountTokenVerificationResult> VerifyAndConsumeAsync(
        string userId,
        AccountTokenType type,
        string token,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token)) {
            return AccountTokenVerificationResult.Invalid;
        }

        string tokenHash = ComputeHash(token);
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();

        List<AccountToken> candidates = await _context
            .AccountTokens.Where(item => item.UserId == userId && item.Type == type)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToListAsync(ct);

        AccountToken? match = candidates.FirstOrDefault(candidate =>
            HashesMatch(candidate.TokenHash, tokenHash)
        );

        if (match is null) {
            return AccountTokenVerificationResult.Invalid;
        }

        return await ConsumeIfValidAsync(match, tokenHash, nowUtc, ct);
    }

    public async Task<TokenVerification> VerifyAndConsumeByTokenAsync(
        AccountTokenType type,
        string token,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace(token)) {
            return new TokenVerification(AccountTokenVerificationResult.Invalid, null);
        }

        string tokenHash = ComputeHash(token);
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();

        AccountToken? match = await _context
            .AccountTokens.FirstOrDefaultAsync(
                item => item.Type == type && item.TokenHash == tokenHash,
                ct
            );

        if (match is null) {
            return new TokenVerification(AccountTokenVerificationResult.Invalid, null);
        }

        AccountTokenVerificationResult result = await ConsumeIfValidAsync(match, tokenHash, nowUtc, ct);
        return new TokenVerification(result, result == AccountTokenVerificationResult.Valid ? match.UserId : null);
    }

    private async Task<AccountTokenVerificationResult> ConsumeIfValidAsync(
        AccountToken match,
        string tokenHash,
        DateTimeOffset nowUtc,
        CancellationToken ct
    ) {
        if (!HashesMatch(match.TokenHash, tokenHash)) {
            return AccountTokenVerificationResult.Invalid;
        }

        if (match.IsExpired(nowUtc)) {
            return AccountTokenVerificationResult.Expired;
        }

        if (match.IsUsed) {
            return AccountTokenVerificationResult.AlreadyUsed;
        }

        match.MarkUsed(nowUtc);

        try {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) {
            // Otro request consumió el token primero.
            return AccountTokenVerificationResult.AlreadyUsed;
        }

        return AccountTokenVerificationResult.Valid;
    }

    private int GetLifetimeMinutes(AccountTokenType type) =>
        type == AccountTokenType.PasswordReset
            ? _options.ResetLifetimeMinutes
            : _options.ActivationLifetimeMinutes;

    private static string CreateRawToken() {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private string ComputeHash(string rawToken) {
        byte[] key = Convert.FromBase64String(_options.PepperKey!);
        byte[] digest = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static bool HashesMatch(string stored, string computed) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(stored),
            Encoding.UTF8.GetBytes(computed)
        );
}
