using System.Security.Cryptography;
using System.Text;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Identity.Security;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;

namespace ArtemisBankingPro.Infrastructure.Identity.Services;

/// <summary>
/// Tokens de activación y restablecimiento: crudos (base64url de 32 bytes
/// aleatorios) entregados por correo, persistiendo solo su hash HMAC-SHA256
/// con pepper. Un solo uso, con vencimiento, vinculados a usuario y propósito.
/// </summary>
public sealed class AccountTokenService : IAccountTokenService {
    private readonly IdentityContext _context;
    private readonly UserManager<AppUser> _userManager;
    private readonly AccountTokenOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly byte[] _pepperKey;
    private readonly ILogger<AccountTokenService> _logger;

    public AccountTokenService(
        IdentityContext context,
        UserManager<AppUser> userManager,
        IOptions<AccountTokenOptions> options,
        TimeProvider timeProvider,
        ILogger<AccountTokenService> logger
    ) {
        _context = context;
        _userManager = userManager;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.PepperKey)) {
            throw new InvalidOperationException(
                "Security:AccountTokens:PepperKey no está configurada. "
                    + "Provea una clave HMAC de 32 bytes en base64."
            );
        }

        try {
            _pepperKey = Convert.FromBase64String(_options.PepperKey);
        }
        catch (FormatException ex) {
            throw new InvalidOperationException(
                "Security:AccountTokens:PepperKey debe estar en base64.",
                ex
            );
        }

        if (_pepperKey.Length < 32) {
            throw new InvalidOperationException(
                "Security:AccountTokens:PepperKey debe contener al menos 32 bytes."
            );
        }

        if (
            _options.ActivationLifetimeMinutes < 1
            || _options.ResetLifetimeMinutes < 1
            || _options.ResetRequestCooldownSeconds < 1
            || _options.ResetRequestWindowMinutes < 1
            || _options.MaxResetRequestsPerWindow < 1
        ) {
            throw new InvalidOperationException(
                "Las vigencias y límites de tokens de cuenta deben ser positivos."
            );
        }
    }

    public async Task<string> GenerateAsync(
        string userId,
        AccountTokenType type,
        CancellationToken ct = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using IDbContextTransaction transaction =
            await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        string rawToken = CreateRawToken();
        if (await LockTokenGenerationAsync(userId, ct) == 0) {
            throw new InvalidOperationException("No existe el usuario para el token de cuenta.");
        }
        // The per-user database lock can wait for another generator. Capture the
        // timestamp after acquiring it so invalidation never predates token creation.
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        await InvalidateActiveTokensAsync(userId, type, nowUtc, ct);
        AddToken(userId, type, rawToken, nowUtc);

        await _context.SaveChangesAsync(ct);
        await transaction.CommitAsync(CancellationToken.None);
        return rawToken;
    }

    public async Task<Result<PasswordResetTokenResult>> GeneratePasswordResetAsync(
        string userId,
        IReadOnlyCollection<string> allowedRoles,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace(userId)) {
            return Result.Failure<PasswordResetTokenResult>(
                DomainError.Validation("Account.ResetUserRequired", "El usuario es requerido.")
            );
        }
        ArgumentNullException.ThrowIfNull(allowedRoles);

        await using IDbContextTransaction transaction =
            await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try {
            if (await LockTokenGenerationAsync(userId, ct) == 0) {
                return Result.Failure<PasswordResetTokenResult>(
                    DomainError.NotFound("User.NotFound", "El usuario no existe.")
                );
            }

            AppUser? user = await _userManager.FindByIdAsync(userId);
            if (user is null) {
                return Result.Failure<PasswordResetTokenResult>(
                    DomainError.NotFound("User.NotFound", "El usuario no existe.")
                );
            }

            IList<string> roles = await _userManager.GetRolesAsync(user);
            if (!roles.Any(allowedRoles.Contains)) {
                return Result.Failure<PasswordResetTokenResult>(
                    DomainError.Validation(
                        "Account.ResetUserNotFound",
                        "No existe un usuario registrado con este nombre de usuario."
                    )
                );
            }

            if (string.IsNullOrWhiteSpace(user.Email)) {
                return Result.Failure<PasswordResetTokenResult>(
                    DomainError.Validation(
                        "Account.ResetEmailMissing",
                        "Este usuario no tiene un correo electrónico registrado. "
                            + "No es posible enviar la solicitud de restablecimiento."
                    )
                );
            }

            DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
            DateTimeOffset cooldownStart = nowUtc.AddSeconds(-_options.ResetRequestCooldownSeconds);
            DateTimeOffset windowStart = nowUtc.AddMinutes(-_options.ResetRequestWindowMinutes);

            AccountToken? latest = await _context.AccountTokens
                .Where(item => item.UserId == userId && item.Type == AccountTokenType.PasswordReset)
                .OrderByDescending(item => item.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);

            if (latest is not null && latest.CreatedAtUtc >= cooldownStart) {
                return Result.Failure<PasswordResetTokenResult>(
                    DomainError.Validation(
                        "Auth.ResetCooldown",
                        "Debe esperar antes de solicitar otro restablecimiento de contraseña."
                    )
                );
            }

            int requestsInWindow = await _context.AccountTokens.CountAsync(
                item =>
                    item.UserId == userId
                    && item.Type == AccountTokenType.PasswordReset
                    && item.CreatedAtUtc >= windowStart,
                ct
            );
            if (requestsInWindow >= _options.MaxResetRequestsPerWindow) {
                return Result.Failure<PasswordResetTokenResult>(
                    DomainError.Validation(
                        "Auth.ResetRateLimited",
                        "Se alcanzó el límite temporal de solicitudes de restablecimiento."
                    )
                );
            }

            string rawToken = CreateRawToken();
            await InvalidateActiveTokensAsync(userId, AccountTokenType.PasswordReset, nowUtc, ct);
            AddToken(userId, AccountTokenType.PasswordReset, rawToken, nowUtc);

            user.Active = false;
            IdentityResult stampResult = await _userManager.UpdateSecurityStampAsync(user);
            if (!stampResult.Succeeded) {
                return Result.Failure<PasswordResetTokenResult>(
                    DomainError.Conflict(
                        "User.StatusUpdateFailed",
                        "No fue posible actualizar el estado del usuario."
                    )
                );
            }

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(CancellationToken.None);
            return Result.Success(
                new PasswordResetTokenResult(rawToken, user.Email, user.FullName)
            );
        }
        catch (DbUpdateConcurrencyException) {
            return Result.Failure<PasswordResetTokenResult>(
                DomainError.Conflict(
                    "Concurrency.Conflict",
                    "La cuenta cambió mientras se generaba el restablecimiento. Intente nuevamente."
                )
            );
        }
        catch (DbUpdateException ex) when (HasSqlServerError(ex, 1205, 2601, 2627)) {
            return Result.Failure<PasswordResetTokenResult>(
                DomainError.Conflict(
                    "Concurrency.Conflict",
                    "La solicitud de restablecimiento entró en conflicto. Intente nuevamente."
                )
            );
        }
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

        return await ConsumeIfValidAsync(match, tokenHash, ct);
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
        AccountToken? match = await _context
            .AccountTokens.FirstOrDefaultAsync(
                item => item.Type == type && item.TokenHash == tokenHash,
                ct
            );

        if (match is null) {
            return new TokenVerification(AccountTokenVerificationResult.Invalid, null);
        }

        AccountTokenVerificationResult result = await ConsumeIfValidAsync(match, tokenHash, ct);
        return new TokenVerification(result, result == AccountTokenVerificationResult.Valid ? match.UserId : null);
    }

    public async Task<Result<AccountTokenVerificationResult>> CompleteActivationAsync(
        string token,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace(token)) {
            return Result.Success(AccountTokenVerificationResult.Invalid);
        }

        string tokenHash = ComputeHash(token);
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        AccountToken? match = await _context.AccountTokens
            .Where(item => item.Type == AccountTokenType.Activation)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(item => item.TokenHash == tokenHash, ct);

        if (match is null || !HashesMatch(match.TokenHash, tokenHash)) {
            return Result.Success(AccountTokenVerificationResult.Invalid);
        }

        if (match.IsExpired(nowUtc)) {
            return Result.Success(AccountTokenVerificationResult.Expired);
        }

        if (match.IsUsed) {
            return Result.Success(AccountTokenVerificationResult.AlreadyUsed);
        }

        AppUser? user = await _userManager.FindByIdAsync(match.UserId);
        if (user is null || user.Active) {
            return Result.Success(AccountTokenVerificationResult.Invalid);
        }

        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();
        try {
            return await strategy.ExecuteAsync(async () => {
                await using var transaction = await _context.Database.BeginTransactionAsync(ct);

                if (await LockTokenGenerationAsync(match.UserId, ct) == 0) {
                    return Result.Success(AccountTokenVerificationResult.Invalid);
                }

                await _context.Entry(user).ReloadAsync(ct);
                if (user.Active) {
                    return Result.Success(AccountTokenVerificationResult.Invalid);
                }

                DateTimeOffset transactionNowUtc = _timeProvider.GetUtcNow();
                if (await TryConsumeAsync(match, transactionNowUtc, ct) != 1) {
                    return Result.Success(
                        transactionNowUtc >= match.ExpiresAtUtc
                            ? AccountTokenVerificationResult.Expired
                            : AccountTokenVerificationResult.AlreadyUsed
                    );
                }

                user.Active = true;
                IdentityResult updateResult = await _userManager.UpdateSecurityStampAsync(user);
                if (!updateResult.Succeeded) {
                    return Result.Failure<AccountTokenVerificationResult>(
                        DomainError.Conflict(
                            "User.StatusUpdateFailed",
                            "No fue posible actualizar el estado del usuario."
                        )
                    );
                }

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(CancellationToken.None);
                return Result.Success(AccountTokenVerificationResult.Valid);
            });
        }
        catch (DbUpdateConcurrencyException) {
            return Result.Failure<AccountTokenVerificationResult>(
                DomainError.Conflict(
                    "Concurrency.Conflict",
                    "La cuenta cambió mientras se procesaba el token. Intente nuevamente."
                )
            );
        }
    }

    public async Task<Result<AccountTokenVerificationResult>> CompletePasswordResetAsync(
        string userId,
        string token,
        string newPassword,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token)) {
            return Result.Success(AccountTokenVerificationResult.Invalid);
        }

        string tokenHash = ComputeHash(token);
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        AccountToken? match = await _context.AccountTokens
            .Where(item => item.UserId == userId && item.Type == AccountTokenType.PasswordReset)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(item => item.TokenHash == tokenHash, ct);

        if (match is null || !HashesMatch(match.TokenHash, tokenHash)) {
            return Result.Success(AccountTokenVerificationResult.Invalid);
        }

        if (match.IsExpired(nowUtc)) {
            return Result.Success(AccountTokenVerificationResult.Expired);
        }

        if (match.IsUsed) {
            return Result.Success(AccountTokenVerificationResult.AlreadyUsed);
        }

        AppUser? user = await _userManager.FindByIdAsync(userId);
        if (user is null) {
            return Result.Failure<AccountTokenVerificationResult>(
                DomainError.NotFound("User.NotFound", "El usuario no existe.")
            );
        }

        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();
        try {
            return await strategy.ExecuteAsync(async () => {
                await using var transaction = await _context.Database.BeginTransactionAsync(ct);

                if (await LockTokenGenerationAsync(userId, ct) == 0) {
                    return Result.Success(AccountTokenVerificationResult.Invalid);
                }

                await _context.Entry(user).ReloadAsync(ct);

                DateTimeOffset transactionNowUtc = _timeProvider.GetUtcNow();
                if (await TryConsumeAsync(match, transactionNowUtc, ct) != 1) {
                    return Result.Success(
                        transactionNowUtc >= match.ExpiresAtUtc
                            ? AccountTokenVerificationResult.Expired
                            : AccountTokenVerificationResult.AlreadyUsed
                    );
                }

                IdentityResult removeResult = await _userManager.RemovePasswordAsync(user);
                if (!removeResult.Succeeded) {
                    return Result.Failure<AccountTokenVerificationResult>(
                        DomainError.Conflict(
                            "User.PasswordChangeFailed",
                            "No fue posible cambiar la contraseña del usuario."
                        )
                    );
                }

                IdentityResult addResult = await _userManager.AddPasswordAsync(user, newPassword);
                if (!addResult.Succeeded) {
                    return Result.Failure<AccountTokenVerificationResult>(
                        PasswordChangeError(addResult, userId)
                    );
                }

                user.Active = true;
                IdentityResult updateResult = await _userManager.UpdateSecurityStampAsync(user);
                if (!updateResult.Succeeded) {
                    return Result.Failure<AccountTokenVerificationResult>(
                        DomainError.Conflict(
                            "User.StatusUpdateFailed",
                            "No fue posible actualizar el estado del usuario."
                        )
                    );
                }

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(CancellationToken.None);
                return Result.Success(AccountTokenVerificationResult.Valid);
            });
        }
        catch (DbUpdateConcurrencyException) {
            return Result.Failure<AccountTokenVerificationResult>(
                DomainError.Conflict(
                    "Concurrency.Conflict",
                    "La cuenta cambió mientras se procesaba el token. Intente nuevamente."
                )
            );
        }
    }

    private async Task<AccountTokenVerificationResult> ConsumeIfValidAsync(
        AccountToken match,
        string tokenHash,
        CancellationToken ct
    ) {
        if (!HashesMatch(match.TokenHash, tokenHash)) {
            return AccountTokenVerificationResult.Invalid;
        }

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        if (match.IsExpired(nowUtc)) {
            return AccountTokenVerificationResult.Expired;
        }

        if (match.IsUsed) {
            return AccountTokenVerificationResult.AlreadyUsed;
        }

        try {
            int affected = await TryConsumeAsync(match, nowUtc, ct);
            if (affected == 1) {
                return AccountTokenVerificationResult.Valid;
            }

            return nowUtc >= match.ExpiresAtUtc
                ? AccountTokenVerificationResult.Expired
                : AccountTokenVerificationResult.AlreadyUsed;
        }
        catch (DbUpdateConcurrencyException) {
            return AccountTokenVerificationResult.AlreadyUsed;
        }
    }

    private async Task InvalidateActiveTokensAsync(
        string userId,
        AccountTokenType type,
        DateTimeOffset usedAtUtc,
        CancellationToken ct
    ) {
        List<AccountToken> previous = await _context.AccountTokens
            .Where(token =>
                token.UserId == userId && token.Type == type && token.UsedAtUtc == null
            )
            .ToListAsync(ct);

        foreach (AccountToken token in previous) {
            token.MarkUsed(usedAtUtc);
        }
    }

    private Task<int> LockTokenGenerationAsync(string userId, CancellationToken ct) =>
        _context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    user => user.AccountTokenVersion,
                    user => user.AccountTokenVersion + 1
                ),
                ct
            );

    private void AddToken(
        string userId,
        AccountTokenType type,
        string rawToken,
        DateTimeOffset nowUtc
    ) {
        _context.AccountTokens.Add(
            new AccountToken(
                userId,
                type,
                ComputeHash(rawToken),
                nowUtc.AddMinutes(GetLifetimeMinutes(type)),
                nowUtc
            )
        );
    }

    private Task<int> TryConsumeAsync(
        AccountToken match,
        DateTimeOffset nowUtc,
        CancellationToken ct
        ) => _context.AccountTokens
        .Where(item =>
            item.Id == match.Id
            && item.UsedAtUtc == null
            && item.ExpiresAtUtc > nowUtc
        )
        .ExecuteUpdateAsync(
            setters => setters.SetProperty(item => item.UsedAtUtc, nowUtc),
            ct
        );

    private int GetLifetimeMinutes(AccountTokenType type) =>
        type == AccountTokenType.PasswordReset
            ? _options.ResetLifetimeMinutes
            : _options.ActivationLifetimeMinutes;

    private DomainError PasswordChangeError(IdentityResult result, string userId) {
        IdentityError[] errors = result.Errors.ToArray();
        string[] unknownCodes = errors
            .Select(error => error.Code)
            .Where(code => code is not (
                "PasswordTooShort"
                or "PasswordRequiresNonAlphanumeric"
                or "PasswordRequiresDigit"
                or "PasswordRequiresLower"
                or "PasswordRequiresUpper"
                or "PasswordRequiresUniqueChars"
            ))
            .ToArray();
        if (unknownCodes.Length > 0) {
            _logger.LogError(
                "Password reset failed for user {UserId}; Identity error codes {ErrorCodes}",
                userId,
                string.Join(",", unknownCodes)
            );
        }

        string[] messages = errors
            .Select(error => error.Code switch {
                "PasswordTooShort" => "La contraseña debe tener al menos 8 caracteres.",
                "PasswordRequiresNonAlphanumeric" => "La contraseña debe incluir al menos un símbolo.",
                "PasswordRequiresDigit" => "La contraseña debe incluir al menos un número.",
                "PasswordRequiresLower" => "La contraseña debe incluir al menos una letra minúscula.",
                "PasswordRequiresUpper" => "La contraseña debe incluir al menos una letra mayúscula.",
                "PasswordRequiresUniqueChars" => "La contraseña debe incluir más caracteres diferentes.",
                _ => null,
            })
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return messages.Length == errors.Length && messages.Length > 0
            ? DomainError.Validation("Auth.PasswordPolicy", string.Join(" ", messages))
            : DomainError.Conflict(
                "User.PasswordChangeFailed",
                "No fue posible cambiar la contraseña del usuario."
            );
    }

    private static string CreateRawToken() {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private string ComputeHash(string rawToken) {
        byte[] digest = HMACSHA256.HashData(_pepperKey, Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static bool HashesMatch(string stored, string computed) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(stored),
            Encoding.UTF8.GetBytes(computed)
        );

    private static bool HasSqlServerError(Exception exception, params int[] numbers) {
        for (Exception? current = exception; current is not null; current = current.InnerException) {
            if (current is SqlException sqlException && numbers.Contains(sqlException.Number)) {
                return true;
            }
        }

        return false;
    }
}
