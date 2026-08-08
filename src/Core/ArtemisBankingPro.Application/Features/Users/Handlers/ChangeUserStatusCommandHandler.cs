using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Users.Handlers;

/// <summary>
/// Activa o inactiva un usuario. La inactivación impide el inicio de sesión
/// pero no elimina productos ni historial. La protección de la propia cuenta
/// la aplica <see cref="Common.Behaviors.AuthorizationBehavior{TRequest,TResponse}"/>
/// vía IOwnershipCheck.
/// </summary>
public sealed class ChangeUserStatusCommandHandler
    : IRequestHandler<ChangeUserStatusCommand, Result<Unit>>
{
    private readonly IUserAccountService _userAccountService;

    public ChangeUserStatusCommandHandler(IUserAccountService userAccountService)
    {
        _userAccountService = userAccountService;
    }

    public async ValueTask<Result<Unit>> Handle(
        ChangeUserStatusCommand message,
        CancellationToken cancellationToken
    )
    {
        var result = await _userAccountService.SetActiveAsync(
            message.UserId,
            message.IsActive,
            cancellationToken
        );

        return result.IsSuccess
            ? Result.Success(Unit.Value)
            : Result.Failure<Unit>(result.Error!);
    }
}
