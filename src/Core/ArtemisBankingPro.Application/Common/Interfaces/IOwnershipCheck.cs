using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Interfaces.Identity;

namespace ArtemisBankingPro.Application.Common.Interfaces;

/// <summary>
/// Verificación de ownership de un recurso antes de ejecutar la operación.
/// Implementado por Commands/Queries cuyos identificadores de recurso deben
/// pertenecer al actor autenticado.
/// </summary>
public interface IOwnershipCheck
{
    /// <summary>
    /// Lanza <see cref="ForbiddenAccessException"/> si el actor no es dueño
    /// del recurso referenciado por el request.
    /// </summary>
    Task VerifyOwnershipAsync(ICurrentUserService currentUser, CancellationToken ct);
}
