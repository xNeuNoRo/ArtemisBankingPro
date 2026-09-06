using ArtemisBankingPro.Application.Common.Results;
using ArtemisBankingPro.Application.Features.Admin.Mapping;
using ArtemisBankingPro.Application.Features.Admin.Queries;
using ArtemisBankingPro.Application.Features.Admin.ViewModels;
using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Features.Users.DTOs;
using ArtemisBankingPro.Application.Features.Users.Mapping;
using ArtemisBankingPro.Application.Features.Users.Queries;
using ArtemisBankingPro.Application.Features.Users.ViewModels;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Merchants.Entities;
using MapsterMapper;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Admin.Services;

public sealed class AdminUserService : IAdminUserService {
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;
    private readonly IGenericService<Merchant> _merchantMaintenance;

    public AdminUserService(
        IMediator mediator,
        IMapper mapper,
        IGenericService<Merchant> merchantMaintenance
    ) {
        _mediator = mediator;
        _mapper = mapper;
        _merchantMaintenance = merchantMaintenance;
    }

    public async Task<Result<AdminDashboardViewModel>> GetDashboardAsync(
        CancellationToken ct = default
    ) => (await _mediator.Send(new GetAdminDashboardQuery(), ct))
        .MapValue(_mapper.Map<AdminDashboardViewModel>);

    public async Task<Result<EligibleClientsViewModel>> GetEligibleClientsAsync(
        EligibleClientsViewModel model,
        ClientAssignmentProduct product,
        int page,
        int pageSize,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        GetEligibleClientsQuery query = AdminMappingRegister.ToQuery(
            model,
            _mapper,
            product,
            page,
            pageSize
        );
        return (await _mediator.Send(query, ct))
            .MapValue(result => AdminMappingRegister.ToViewModel(
                result,
                _mapper,
                model.Identification,
                model.SelectedClientId
            ));
    }

    public async Task<Result<UserListViewModel>> GetUsersAsync(
        UserListViewModel model,
        int page,
        int pageSize,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        Result<PageResult<UserListResponse>> result = await _mediator.Send(
            new GetUsersPagedQuery(page, pageSize, model.Role),
            ct
        );
        return result.MapValue(paged => new UserListViewModel {
            Role = model.Role,
            RoleOptions = UserListViewModel.BuildRoleOptions(model.Role),
            Users = paged.Items.Select(_mapper.Map<UserListItemViewModel>).ToArray(),
            Pagination = new PaginationViewModel {
                Page = paged.Page,
                PageSize = paged.PageSize,
                TotalItems = paged.TotalCount,
            },
        });
    }

    public async Task<Result<UserDetailViewModel>> GetUserAsync(
        string userId,
        CancellationToken ct = default
    ) => (await _mediator.Send(new GetUserByIdQuery(userId), ct))
        .MapValue(_mapper.Map<UserDetailViewModel>);

    public async Task<Result<UserCreationOutcomeViewModel>> CreateUserAsync(
        CreateUserViewModel model,
        string idempotencyKey,
        string? callbackUrl = null,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        CreateUserCommand command = UsersMappingRegister.ToCreateCommand(
            model,
            _mapper,
            idempotencyKey,
            callbackUrl
        );
        return (await _mediator.Send(command, ct)).MapValue(response =>
            new UserCreationOutcomeViewModel {
                ActivationEmailSent = response.ActivationEmailSent,
            }
        );
    }

    public async Task<Result> CreateCommerceUserAsync(
        CreateCommerceUserViewModel model,
        int commerceId,
        string idempotencyKey,
        string? callbackUrl = null,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        if (!await _merchantMaintenance.ExistsAsync(merchant => merchant.Id == commerceId, ct)) {
            return Result.Failure(
                DomainError.NotFound(
                    "Commerce.NotFound",
                    "El comercio indicado no existe."
                )
            );
        }

        CreateCommerceUserCommand command = UsersMappingRegister.ToCreateCommerceCommand(
            model,
            _mapper,
            commerceId,
            idempotencyKey,
            callbackUrl
        );
        return (await _mediator.Send(command, ct)).ToUnit();
    }

    public async Task<Result> UpdateUserAsync(
        string userId,
        UpdateUserViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        UpdateUserCommand command = UsersMappingRegister.ToUpdateCommand(
            model,
            _mapper,
            userId,
            idempotencyKey
        );
        return (await _mediator.Send(command, ct)).ToUnit();
    }

    public async Task<Result> ChangeUserStatusAsync(
        string userId,
        ChangeUserStatusViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        ChangeUserStatusCommand command = UsersMappingRegister.ToChangeStatusCommand(
            model,
            _mapper,
            userId,
            idempotencyKey
        );
        return (await _mediator.Send(command, ct)).ToUnit();
    }
}
