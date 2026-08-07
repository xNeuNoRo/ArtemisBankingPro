namespace ArtemisBankingPro.Infrastructure.Shared.Security;

/// <summary>
/// Servicio de seguridad de tarjetas: huella del número (fingerprint) y
/// digest del CVC. Nunca cifra el PAN porque el número completo no se
/// persiste en ningún almacén de la aplicación.
/// Implementación interna de la infraestructura compartida.
/// </summary>
public interface ICardSecurityService
{
    /// <summary>
    /// Huella HMAC-SHA256 en hexadecimal (64 caracteres) del número de tarjeta
    /// normalizado. Es la clave de búsqueda única de una tarjeta.
    /// </summary>
    string ComputePanFingerprint(string pan);

    /// <summary>
    /// Digest HMAC-SHA256 en hexadecimal (64 caracteres) del CVC con pepper.
    /// </summary>
    string ComputeCvcDigest(string cvc);

    /// <summary>
    /// Verifica el CVC contra el digest almacenado con comparación de tiempo constante.
    /// </summary>
    bool VerifyCvc(string cvc, string storedDigest);
}
