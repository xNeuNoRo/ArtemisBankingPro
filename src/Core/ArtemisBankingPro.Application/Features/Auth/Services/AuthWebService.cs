using ArtemisBankingPro.Application.Common.Results;
using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.DTOs;
using ArtemisBankingPro.Application.Features.Auth.Mapping;
using ArtemisBankingPro.Application.Features.Auth.ViewModels;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using MapsterMapper;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Auth.Services;

public sealed class AuthWebService : IAuthWebService {
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;
    private readonly IUserAccountService _userAccountService;

    public AuthWebService(
        IMediator mediator,
        IMapper mapper,
        IUserAccountService userAccountService
    ) {
        _mediator = mediator;
        _mapper = mapper;
        _userAccountService = userAccountService;
    }

    public async Task<Result<WebAppLoginResponse>> LoginAsync(
        LoginViewModel model,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);

        Result<WebAppLoginResponse> result = await _mediator.Send(
            _mapper.Map<WebAppLoginCommand>(model),
            ct
        );
        if (result.IsFailure) {
            return result;
        }

        Result signInResult = await _userAccountService.SignInWebAppAsync(
            result.Value.UserId,
            result.Value.Role,
            ct
        );
        return signInResult.IsSuccess
            ? result
            : Result.Failure<WebAppLoginResponse>(signInResult.Error!);
    }

    public Task<Result> SignOutAsync(CancellationToken ct = default) =>
        _userAccountService.SignOutWebAppAsync(ct);

    public async Task<Result> ActivateAccountAsync(
        ActivateAccountViewModel model,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        return (await _mediator.Send(_mapper.Map<ActivateAccountCommand>(model), ct)).ToUnit();
    }

    public async Task<Result> RequestPasswordResetAsync(
        RequestPasswordResetViewModel model,
        string? callbackUrl = null,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        RequestPasswordResetCommand command = model.ToRequestPasswordResetCommand(
            RoleSets.Mvc,
            callbackUrl
        );
        return (await _mediator.Send(command, ct)).ToUnit();
    }

    public async Task<Result> ResetPasswordAsync(
        ResetPasswordViewModel model,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        return (await _mediator.Send(_mapper.Map<ResetPasswordCommand>(model), ct)).ToUnit();
    }
}
