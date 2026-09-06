using ArtemisBankingPro.Application.Features.Admin.ViewModels;
using ArtemisBankingPro.Application.Features.Admin.Queries;
using ArtemisBankingPro.Application.Features.Users.ViewModels;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Features.Admin.Services;

public interface IAdminUserService {
    Task<Result<AdminDashboardViewModel>> GetDashboardAsync(CancellationToken ct = default);

    Task<Result<EligibleClientsViewModel>> GetEligibleClientsAsync(
        EligibleClientsViewModel model,
        ClientAssignmentProduct product,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<Result<UserListViewModel>> GetUsersAsync(
        UserListViewModel model,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<Result<UserDetailViewModel>> GetUserAsync(
        string userId,
        CancellationToken ct = default
    );

    Task<Result<UserCreationOutcomeViewModel>> CreateUserAsync(
        CreateUserViewModel model,
        string idempotencyKey,
        string? callbackUrl = null,
        CancellationToken ct = default
    );

    Task<Result> CreateCommerceUserAsync(
        CreateCommerceUserViewModel model,
        int commerceId,
        string idempotencyKey,
        string? callbackUrl = null,
        CancellationToken ct = default
    );

    Task<Result> UpdateUserAsync(
        string userId,
        UpdateUserViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );

    Task<Result> ChangeUserStatusAsync(
        string userId,
        ChangeUserStatusViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );
}
