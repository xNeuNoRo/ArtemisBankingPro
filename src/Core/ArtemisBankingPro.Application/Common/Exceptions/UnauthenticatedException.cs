namespace ArtemisBankingPro.Application.Common.Exceptions;

/// <summary>
/// Se lanza cuando el actor no está autenticado. Se traduce a 401 Unauthorized
/// en la capa de presentación.
/// </summary>
public sealed class UnauthenticatedException(
    string message = "Debe iniciar sesión para realizar esta acción."
) : Exception(message);
