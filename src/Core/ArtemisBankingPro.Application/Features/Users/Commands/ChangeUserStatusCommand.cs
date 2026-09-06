using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Users.Commands;

/// <summary>
/// Activa o inactiva un usuario. El administrador no puede modificar el estado
/// de su propia cuenta.
/// </summary>
public sealed record ChangeUserStatusCommand(
    string UserId,
    bool IsActive
) : IRequest<Result<Unit>>, IAuthorize, IIdempotentCommand, IOwnershipCheck {
    public string[] RequiredRoles => ["Administrador"];

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestFingerprint => $"{UserId}|{IsActive}";

    public Task VerifyOwnershipAsync(ICurrentUserService currentUser, CancellationToken ct) {
        if (currentUser.UserId == UserId) {
            throw new ForbiddenAccessException(
                "No puede modificar el estado de su propia cuenta."
            );
        }

        return Task.CompletedTask;
    }
}
