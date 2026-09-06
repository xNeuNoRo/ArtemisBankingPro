using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Api.Extensions;
using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Features.Users.DTOs;
using ArtemisBankingPro.Application.Features.Users.Mapping;
using ArtemisBankingPro.Application.Features.Users.Queries;
using ArtemisBankingPro.Application.Features.Users.Requests;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.Api.Controllers;

[ApiController]
[Authorize(Policy = ApiAuthorizationPolicies.Administrator)]
[Route("api/users")]
public sealed class UsersController(
    IMediator mediator,
    IErrorResponseMapper errorMapper
) : ApiControllerBase(errorMapper) {
    [HttpGet]
    [ProducesResponseType(typeof(PagedApiResponse<UserListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string? role = null,
        CancellationToken cancellationToken = default
    ) {
        Result<PageResult<UserListResponse>> result = await mediator.Send(
            new GetUsersPagedQuery(page, pageSize, role),
            cancellationToken
        );

        return result.IsSuccess ? Ok(ToResponse(result.Value)) : ToProblem(result.Error!);
    }

    [HttpGet("commerce")]
    [ProducesResponseType(typeof(PagedApiResponse<CommerceUserListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCommerceUsers(
        [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default
    ) {
        Result<PageResult<CommerceUserListResponse>> result = await mediator.Send(
            new GetCommerceUsersPagedQuery(page, pageSize),
            cancellationToken
        );

        return result.IsSuccess ? Ok(ToResponse(result.Value)) : ToProblem(result.Error!);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(UserDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(
        string id,
        CancellationToken cancellationToken
    ) {
        Result<UserDetailResponse> result = await mediator.Send(
            new GetUserByIdQuery(id),
            cancellationToken
        );

        return result.IsSuccess ? Ok(result.Value) : ToProblem(result.Error!);
    }

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CreateUserResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateUserApiRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken
    ) {
        CreateUserCommand command = UsersMappingRegister.ToCreateApiCommand(
            request,
            idempotencyKey ?? string.Empty
        );
        Result<CreateUserResponse> result = await mediator.Send(command, cancellationToken);

        return result.IsFailure
            ? ToProblem(result.Error!)
            : CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPost("commerce/{commerceId:int}")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CreateCommerceUserResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCommerceUser(
        int commerceId,
        [FromBody] CreateCommerceUserApiRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken
    ) {
        CreateCommerceUserCommand command = UsersMappingRegister.ToCreateCommerceApiCommand(
            request,
            commerceId,
            idempotencyKey ?? string.Empty
        );
        Result<CreateCommerceUserResponse> result = await mediator.Send(command, cancellationToken);

        return result.IsFailure
            ? ToProblem(result.Error!)
            : CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id}")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(
        string id,
        [FromBody] UpdateUserApiRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken
    ) {
        UpdateUserCommand command = UsersMappingRegister.ToUpdateApiCommand(
            request,
            id,
            idempotencyKey ?? string.Empty
        );
        Result<Unit> result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? NoContent() : ToProblem(result.Error!);
    }

    [HttpPatch("{id}/status")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangeStatus(
        string id,
        [FromBody] ChangeUserStatusApiRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken
    ) {
        ChangeUserStatusCommand command = UsersMappingRegister.ToChangeStatusApiCommand(
            request,
            id,
            idempotencyKey ?? string.Empty
        );
        Result<Unit> result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? NoContent() : ToProblem(result.Error!);
    }

    private static PagedApiResponse<UserListResponse> ToResponse(PageResult<UserListResponse> page) =>
        new(page.Page, page.PageSize, page.TotalCount, page.TotalPages, page.Items);

    private static PagedApiResponse<CommerceUserListResponse> ToResponse(
        PageResult<CommerceUserListResponse> page
    ) => new(page.Page, page.PageSize, page.TotalCount, page.TotalPages, page.Items);
}
