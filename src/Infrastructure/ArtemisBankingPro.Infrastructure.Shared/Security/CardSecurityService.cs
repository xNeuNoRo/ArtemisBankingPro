using System.Security.Cryptography;
using System.Text;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Domain.Cards.Security;
using Microsoft.Extensions.Options;

namespace ArtemisBankingPro.Infrastructure.Shared.Security;

/// <summary>
/// Derivados seguros de datos de tarjeta. El PAN nunca se persiste: solo su
/// huella HMAC con clave (búsqueda) y los últimos cuatro dígitos (visualización).
/// El CVC se almacena como digest HMAC con pepper, no como SHA-256 plano
/// (el espacio de 3 dígitos es trivial de enumerar).
/// </summary>
public sealed class CardSecurityService : ICardSecurityService, ICvcVerifier {
    private readonly byte[] _fingerprintKey;
    private readonly byte[] _cvcPepperKey;

    public CardSecurityService(IOptions<CardSecurityOptions> options) {
        CardSecurityOptions configured = options.Value;
        _fingerprintKey = GetKey(configured.FingerprintKey, nameof(configured.FingerprintKey));
        _cvcPepperKey = GetKey(configured.CvcPepperKey, nameof(configured.CvcPepperKey));
    }

    public string ComputePanFingerprint(string pan) {
        ArgumentException.ThrowIfNullOrWhiteSpace(pan);

        byte[] digest = HMACSHA256.HashData(_fingerprintKey, Encoding.UTF8.GetBytes(NormalizePan(pan)));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    public string ComputeCvcDigest(string cvc) {
        if (!IsValidCvc(cvc)) {
            throw new ArgumentException("El CVC debe contener exactamente tres dígitos.", nameof(cvc));
        }

        byte[] digest = HMACSHA256.HashData(_cvcPepperKey, Encoding.UTF8.GetBytes(cvc));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    public bool VerifyCvc(string cvc, string storedDigest) {
        if (!IsValidCvc(cvc) || string.IsNullOrWhiteSpace(storedDigest)) {
            return false;
        }

        string computed = ComputeCvcDigest(cvc);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(storedDigest)
        );
    }

    public bool Verify(string cvc, string storedDigest) => VerifyCvc(cvc, storedDigest);

    private static byte[] GetKey(string? key, string keyName) {
        if (string.IsNullOrWhiteSpace(key)) {
            throw new InvalidOperationException(
                $"Security:Card:{keyName} no está configurada. Provea una clave HMAC de 32 bytes en base64."
            );
        }

        try {
            byte[] decoded = Convert.FromBase64String(key);
            if (decoded.Length < 32) {
                throw new InvalidOperationException(
                    $"Security:Card:{keyName} debe contener al menos 32 bytes."
                );
            }

            return decoded;
        }
        catch (FormatException ex) {
            throw new InvalidOperationException(
                $"Security:Card:{keyName} debe estar en base64.",
                ex
            );
        }
    }

    private static string NormalizePan(string pan) {
        ArgumentException.ThrowIfNullOrWhiteSpace(pan);

        if (pan.Any(character =>
                !char.IsAsciiDigit(character)
                && character is not ' '
                && character is not '-')) {
            throw new ArgumentException("El número de tarjeta contiene caracteres inválidos.", nameof(pan));
        }

        string digits = string.Concat(pan.Where(char.IsAsciiDigit));
        if (digits.Length != 16) {
            throw new ArgumentException("El número de tarjeta debe contener 16 dígitos.", nameof(pan));
        }

        return digits;
    }

    private static bool IsValidCvc(string? cvc) =>
        cvc is { Length: 3 } && cvc.All(char.IsAsciiDigit);
}
