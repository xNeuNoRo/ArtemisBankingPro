using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Interfaces.Identity;
using Mediator;

namespace ArtemisBankingPro.Application.Common.Behaviors;

/// <summary>
/// Aplica autenticación, autorización por rol y verificación de ownership
/// para cada request que implemente <see cref="IAuthorize"/>, en ese orden.
/// Solo aplica a requests que implementen la interfaz.
/// </summary>
public sealed class AuthorizationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IAuthorize {
    private readonly ICurrentUserService _currentUser;

    public AuthorizationBehavior(ICurrentUserService currentUser) => _currentUser = currentUser;

    public async ValueTask<TResponse> Handle(
        TRequest message,
        MessageHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken
    ) {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null) {
            throw new UnauthenticatedException();
        }

        if (
            message.RequiredRoles.Length > 0
            && (_currentUser.Role is null || !message.RequiredRoles.Contains(_currentUser.Role))
        ) {
            throw new ForbiddenAccessException(
                $"El rol '{_currentUser.Role}' no tiene permisos para esta operación."
            );
        }

        if (message is IOwnershipCheck ownershipCheck) {
            await ownershipCheck.VerifyOwnershipAsync(_currentUser, cancellationToken);
        }

        return await next(message, cancellationToken);
    }
}
