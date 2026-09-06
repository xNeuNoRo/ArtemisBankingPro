using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.ViewModels;
using Mapster;

namespace ArtemisBankingPro.Application.Features.Auth.Mapping;

/// <summary>
/// Explicit MVC input mappings for authentication use cases.
/// </summary>
public sealed class AuthMappingRegister : IRegister {
    public void Register(TypeAdapterConfig config) {
        config.NewConfig<LoginViewModel, WebAppLoginCommand>()
            .MapWith(source => new WebAppLoginCommand(source.UserName, source.Password));

        config.NewConfig<ResetPasswordViewModel, ResetPasswordCommand>()
            .MapWith(source => new ResetPasswordCommand(
                source.UserId,
                source.Token,
                source.Password,
                source.ConfirmPassword
            ));

        config.NewConfig<ActivateAccountViewModel, ActivateAccountCommand>()
            .MapWith(source => new ActivateAccountCommand(source.Token));
    }
}
