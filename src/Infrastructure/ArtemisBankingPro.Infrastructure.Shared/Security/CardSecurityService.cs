using System.Security.Cryptography;
using System.Text;
using ArtemisBankingPro.Application.Interfaces.Security;
using Microsoft.Extensions.Options;

namespace ArtemisBankingPro.Infrastructure.Shared.Security;

/// <summary>
/// Derivados seguros de datos de tarjeta. El PAN nunca se persiste: solo su
/// huella HMAC con clave (búsqueda) y los últimos cuatro dígitos (visualización).
/// El CVC se almacena como digest HMAC con pepper, no como SHA-256 plano
/// (el espacio de 3 dígitos es trivial de enumerar).
/// </summary>
public sealed class CardSecurityService : ICardSecurityService {
    private readonly CardSecurityOptions _options;

    public CardSecurityService(IOptions<CardSecurityOptions> options) {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.FingerprintKey)) {
            throw new InvalidOperationException(
                "Security:Card:FingerprintKey no está configurada. "
                    + "Provea una clave HMAC de 32 bytes en base64."
            );
        }

        if (string.IsNullOrWhiteSpace(_options.CvcPepperKey)) {
            throw new InvalidOperationException(
                "Security:Card:CvcPepperKey no está configurada. "
                    + "Provea una clave HMAC de 32 bytes en base64."
            );
        }
    }

    public string ComputePanFingerprint(string pan) {
        ArgumentException.ThrowIfNullOrWhiteSpace(pan);

        byte[] key = GetKey(_options.FingerprintKey);
        byte[] digest = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(NormalizeDigits(pan)));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    public string ComputeCvcDigest(string cvc) {
        ArgumentException.ThrowIfNullOrWhiteSpace(cvc);

        byte[] key = GetKey(_options.CvcPepperKey);
        byte[] digest = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(cvc.Trim()));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    public bool VerifyCvc(string cvc, string storedDigest) {
        if (string.IsNullOrWhiteSpace(cvc) || string.IsNullOrWhiteSpace(storedDigest)) {
            return false;
        }

        string computed = ComputeCvcDigest(cvc);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(storedDigest)
        );
    }

    private static byte[] GetKey(string? key) {
        try {
            return Convert.FromBase64String(key!);
        }
        catch (FormatException ex) {
            throw new InvalidOperationException(
                "Las claves de seguridad de tarjeta deben estar en base64.",
                ex
            );
        }
    }

    private static string NormalizeDigits(string pan) =>
        string.Concat(pan.Where(char.IsAsciiDigit));
}
