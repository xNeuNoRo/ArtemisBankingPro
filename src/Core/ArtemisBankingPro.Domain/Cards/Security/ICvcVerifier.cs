namespace ArtemisBankingPro.Domain.Cards.Security;

/// <summary>
/// Verifica un CVC contra su digest almacenado mediante comparación en tiempo
/// fijo. Implementado por la infraestructura con HMAC-SHA256 + pepper.
/// </summary>
public interface ICvcVerifier {
    bool Verify(string cvc, string storedDigest);
}
