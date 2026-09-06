namespace ArtemisBankingPro.Application.Common.Exceptions;

/// <summary>
/// Se lanza cuando el actor autenticado no tiene el rol requerido o no posee
/// el recurso solicitado. Se traduce a 403 Forbidden en la capa de presentación.
/// </summary>
public sealed class ForbiddenAccessException(string message = "Acceso denegado.")
    : Exception(message);
