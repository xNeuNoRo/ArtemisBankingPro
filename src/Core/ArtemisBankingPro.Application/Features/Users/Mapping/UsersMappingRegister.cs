using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Features.Users.DTOs;
using ArtemisBankingPro.Application.Features.Users.Queries;
using ArtemisBankingPro.Application.Features.Users.ViewModels;
using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Pagination;
using Mapster;
using MapsterMapper;

namespace ArtemisBankingPro.Application.Features.Users.Mapping;

public sealed class UsersMappingRegister : IRegister {
    public void Register(TypeAdapterConfig config) {
        config.NewConfig<UserListDto, UserListItemViewModel>();
        config.NewConfig<UserMainAccountResponse, UserMainAccountViewModel>();
        config.NewConfig<UserDetailResponse, UserDetailViewModel>();
        config.NewConfig<CreateUserViewModel, CreateUserCommand>()
            .MapWith(source => new CreateUserCommand(
                source.FirstName,
                source.LastName,
                source.Identification,
                source.Email,
                source.UserName,
                source.Password,
                source.ConfirmPassword,
                source.Role,
                source.InitialAmount,
                null
            ));
        config.NewConfig<CreateCommerceUserViewModel, CreateCommerceUserCommand>()
            .MapWith(source => new CreateCommerceUserCommand(
                0,
                source.FirstName,
                source.LastName,
                source.Identification,
                source.Email,
                source.UserName,
                source.Password,
                source.ConfirmPassword,
                source.InitialAmount,
                null
            ));
        config.NewConfig<UpdateUserViewModel, UpdateUserCommand>()
            .MapWith(source => new UpdateUserCommand(
                string.Empty,
                source.FirstName,
                source.LastName,
                source.Identification,
                source.Email,
                source.UserName,
                source.Password,
                source.ConfirmPassword,
                source.AdditionalAmount
            ));
        config.NewConfig<UserListViewModel, GetUsersPagedQuery>()
            .MapWith(source => new GetUsersPagedQuery(
                PageRequest.DefaultPage,
                PageRequest.DefaultPageSize,
                source.Role
            ));
        config.NewConfig<ChangeUserStatusViewModel, ChangeUserStatusCommand>()
            .MapWith(source => new ChangeUserStatusCommand(string.Empty, source.IsActive));
        config.NewConfig<UserDetailViewModel, GetUserByIdQuery>()
            .MapWith(_ => new GetUserByIdQuery(string.Empty));
    }

    public static CreateUserCommand ToCreateCommand(
        CreateUserViewModel source,
        IMapper mapper,
        string idempotencyKey,
        string? callbackUrl
    ) => mapper.Map<CreateUserCommand>(source) with {
        IdempotencyKey = idempotencyKey,
        CallbackUrl = callbackUrl,
    };

    public static CreateCommerceUserCommand ToCreateCommerceCommand(
        CreateCommerceUserViewModel source,
        IMapper mapper,
        int commerceId,
        string idempotencyKey,
        string? callbackUrl
    ) => mapper.Map<CreateCommerceUserCommand>(source) with {
        CommerceId = commerceId,
        IdempotencyKey = idempotencyKey,
        CallbackUrl = callbackUrl,
    };

    public static UpdateUserCommand ToUpdateCommand(
        UpdateUserViewModel source,
        IMapper mapper,
        string userId,
        string idempotencyKey
    ) => mapper.Map<UpdateUserCommand>(source) with {
        UserId = userId,
        IdempotencyKey = idempotencyKey,
    };

    public static ChangeUserStatusCommand ToChangeStatusCommand(
        ChangeUserStatusViewModel source,
        IMapper mapper,
        string userId,
        string idempotencyKey
    ) => mapper.Map<ChangeUserStatusCommand>(source) with {
        UserId = userId,
        IdempotencyKey = idempotencyKey,
    };

    public static GetUsersPagedQuery ToListQuery(
        UserListViewModel source,
        IMapper mapper,
        int page,
        int pageSize
    ) => mapper.Map<GetUsersPagedQuery>(source) with {
        Page = page,
        PageSize = pageSize,
    };

    public static GetUserByIdQuery ToDetailQuery(
        UserDetailViewModel source,
        IMapper mapper,
        string userId
    ) => mapper.Map<GetUserByIdQuery>(source) with {
        UserId = userId,
    };

    public static UserListViewModel ToListViewModel(
        IEnumerable<UserListDto> users,
        int page,
        int pageSize,
        int totalCount,
        IMapper mapper,
        string? role = null
    ) => new() {
        Role = role,
        Users = users.Select(mapper.Map<UserListItemViewModel>).ToArray(),
        Pagination = new PaginationViewModel {
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
        },
    };
}
