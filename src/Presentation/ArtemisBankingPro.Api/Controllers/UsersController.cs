using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Application.Features.Users.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrador")]
[Route("api/users")]
public sealed class UsersController(
    IMediator mediator,
    IErrorResponseMapper errorMapper
) : ApiControllerBase(errorMapper) {
    [HttpGet]
    [ProducesResponseType(typeof(PagedApiResponse<UserListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string? role = null,
        CancellationToken cancellationToken = default
    ) {
        Result<PageResult<UserListDto>> result = await mediator.Send(
            new GetUsersPagedQuery(page, pageSize, role),
            cancellationToken
        );

        return result.IsSuccess ? Ok(ToResponse(result.Value)) : ToProblem(result.Error!);
    }

    [HttpGet("commerce")]
    [ProducesResponseType(typeof(PagedApiResponse<UserListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCommerceUsers(
        [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default
    ) {
        Result<PageResult<UserListDto>> result = await mediator.Send(
            new GetCommerceUsersPagedQuery(page, pageSize),
            cancellationToken
        );

        return result.IsSuccess ? Ok(ToResponse(result.Value)) : ToProblem(result.Error!);
    }

    private static PagedApiResponse<UserListDto> ToResponse(PageResult<UserListDto> page) =>
        new(page.Page, page.PageSize, page.TotalCount, page.TotalPages, page.Items);
}
