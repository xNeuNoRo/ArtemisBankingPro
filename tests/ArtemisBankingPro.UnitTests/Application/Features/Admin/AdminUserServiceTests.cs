using System.Linq.Expressions;
using ArtemisBankingPro.Application.Common.Mapping;
using ArtemisBankingPro.Application.Features.Admin.Services;
using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Features.Users.DTOs;
using ArtemisBankingPro.Application.Features.Users.Queries;
using ArtemisBankingPro.Application.Features.Users.ViewModels;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Merchants.Entities;
using MapsterMapper;
using Mediator;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Admin;

public sealed class AdminUserServiceTests {
    [Fact]
    public async Task User_list_maps_the_application_response_to_a_safe_web_view_model() {
        var mediator = new Mock<IMediator>();
        mediator.Setup(item => item.Send(
                It.IsAny<GetUsersPagedQuery>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns(new ValueTask<Result<PageResult<UserListResponse>>>(Result.Success(
                new PageResult<UserListResponse>(
                    [new("user-1", "ana", "001", "Ana", "Perez", "ana@example.com", "Cliente", true)],
                    21,
                    2,
                    20
                )
            )));

        var service = new AdminUserService(
            mediator.Object,
            new ServiceMapper(null!, MapsterConfig.Create()),
            new Mock<IGenericService<Merchant>>().Object
        );

        UserListViewModel result = (await service.GetUsersAsync(
            new UserListViewModel { Role = "Cliente" },
            2,
            20
        )).Value;

        result.Users.Should().ContainSingle();
        result.Users[0].UserId.Should().Be("user-1");
        result.Users[0].UserName.Should().Be("ana");
        result.Pagination.TotalItems.Should().Be(21);
        result.Pagination.Page.Should().Be(2);
        mediator.Verify(item => item.Send(
            It.Is<GetUsersPagedQuery>(query =>
                query.Page == 2 && query.PageSize == 20 && query.Role == "Cliente"),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task Missing_commerce_is_rejected_before_sending_the_creation_command() {
        var mediator = new Mock<IMediator>();
        var merchantMaintenance = new Mock<IGenericService<Merchant>>();
        merchantMaintenance
            .Setup(item => item.ExistsAsync(
                It.IsAny<Expression<Func<Merchant, bool>>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(false);

        var service = new AdminUserService(
            mediator.Object,
            new ServiceMapper(null!, MapsterConfig.Create()),
            merchantMaintenance.Object
        );

        Result result = await service.CreateCommerceUserAsync(
            new CreateCommerceUserViewModel(),
            7,
            "idempotency-key"
        );

        result.Error!.Code.Should().Be("Commerce.NotFound");
        mediator.Verify(
            item => item.Send(
                It.IsAny<CreateCommerceUserCommand>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }
}
