namespace ArtemisBankingPro.Application.Interfaces.Security;

/// <summary>
/// Servicio de seguridad para tarjetas de crédito y débito.
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

    /// <summassry>
    /// Verifica el CVC contra el digest almacenado con comparación de tiempo constante.
    /// </summassry>
    bool VerifyCvc(string cvc, string storedDigest);
}
