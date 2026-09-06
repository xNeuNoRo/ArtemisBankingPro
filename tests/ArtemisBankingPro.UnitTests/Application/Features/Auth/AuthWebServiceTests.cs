using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.DTOs;
using ArtemisBankingPro.Application.Features.Auth.Services;
using ArtemisBankingPro.Application.Features.Auth.ViewModels;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Common.Mapping;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using MapsterMapper;
using Mediator;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Auth;

public sealed class AuthWebServiceTests {
    [Fact]
    public async Task Login_revalidates_identity_before_returning_success() {
        var mediator = new Mock<IMediator>();
        var accountService = new Mock<IUserAccountService>();
        mediator.Setup(item => item.Send(
                It.IsAny<WebAppLoginCommand>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns(new ValueTask<Result<WebAppLoginResponse>>(Result.Success(
                new WebAppLoginResponse("user-1", "cliente", "Cliente")
            )));
        accountService.Setup(item => item.SignInWebAppAsync(
                "user-1",
                "Cliente",
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Result.Success());

        var service = new AuthWebService(
            mediator.Object,
            new ServiceMapper(null!, MapsterConfig.Create()),
            accountService.Object
        );

        Result<WebAppLoginResponse> result = await service.LoginAsync(new LoginViewModel {
            UserName = "cliente",
            Password = "P@ssw0rd123!",
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be("user-1");
        accountService.Verify(item => item.SignInWebAppAsync(
            "user-1",
            "Cliente",
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task Login_does_not_return_success_when_cookie_issuance_revalidation_fails() {
        var mediator = new Mock<IMediator>();
        var accountService = new Mock<IUserAccountService>();
        mediator.Setup(item => item.Send(
                It.IsAny<WebAppLoginCommand>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns(new ValueTask<Result<WebAppLoginResponse>>(Result.Success(
                new WebAppLoginResponse("user-1", "comercio", "Comercio")
            )));
        accountService.Setup(item => item.SignInWebAppAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Result.Failure(DomainError.Forbidden(
                "Auth.RoleNotAllowed",
                "Este usuario no tiene permisos para acceder a la aplicación web."
            )));

        var service = new AuthWebService(
            mediator.Object,
            new ServiceMapper(null!, MapsterConfig.Create()),
            accountService.Object
        );

        Result<WebAppLoginResponse> result = await service.LoginAsync(new LoginViewModel {
            UserName = "comercio",
            Password = "P@ssw0rd123!",
        });

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Auth.RoleNotAllowed");
    }
}
