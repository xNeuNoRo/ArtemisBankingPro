using ArtemisBankingPro.Application.Features.Auth.DTOs;
using ArtemisBankingPro.Application.Features.Auth.ViewModels;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Features.Auth.Services;

public interface IAuthWebService {
    Task<Result<WebAppLoginResponse>> LoginAsync(
        LoginViewModel model,
        CancellationToken ct = default
    );

    Task<Result> SignOutAsync(CancellationToken ct = default);

    Task<Result> ActivateAccountAsync(
        ActivateAccountViewModel model,
        CancellationToken ct = default
    );

    Task<Result> RequestPasswordResetAsync(
        RequestPasswordResetViewModel model,
        string? callbackUrl = null,
        CancellationToken ct = default
    );

    Task<Result> ResetPasswordAsync(
        ResetPasswordViewModel model,
        CancellationToken ct = default
    );
}
