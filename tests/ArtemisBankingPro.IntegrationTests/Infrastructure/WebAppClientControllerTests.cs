extern alias WebApp;

using ArtemisBankingPro.Application.Features.Client.Services;
using ArtemisBankingPro.Application.Features.Client.ViewModels;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebApp::ArtemisBankingPro.WebApp.Controllers;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

public sealed class WebAppClientControllerTests {
    [Theory]
    [InlineData("express")]
    [InlineData("beneficiary")]
    [InlineData("own-accounts")]
    [InlineData("card")]
    [InlineData("loan")]
    [InlineData("cash-advance")]
    public async Task Successful_financial_confirmations_redirect_to_client_home(string operation) {
        Mock<IClientOperationsService> client = new();
        client.Setup(service => service.ExpressTransferAsync(
                It.IsAny<ExpressTransactionViewModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Result.Success());
        client.Setup(service => service.BeneficiaryTransferAsync(
                It.IsAny<BeneficiaryTransferViewModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Result.Success());
        client.Setup(service => service.OwnAccountsTransferAsync(
                It.IsAny<OwnAccountsTransferViewModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Result.Success());
        client.Setup(service => service.PayCardAsync(
                It.IsAny<ClientCardPaymentViewModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Result.Success());
        client.Setup(service => service.PayLoanAsync(
                It.IsAny<ClientLoanPaymentViewModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Result.Success());
        client.Setup(service => service.CashAdvanceAsync(
                It.IsAny<CashAdvanceViewModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Result.Success());

        var controller = new ClientController(
            client.Object,
            Mock.Of<IConfirmationTokenService>(),
            NullLogger<ClientController>.Instance
        );
        controller.ControllerContext = new ControllerContext {
            HttpContext = new DefaultHttpContext(),
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            Mock.Of<ITempDataProvider>()
        );

        IActionResult result = operation switch {
            "express" => await controller.ConfirmExpressTransfer(Confirmation()),
            "beneficiary" => await controller.ConfirmBeneficiaryTransfer(Confirmation()),
            "own-accounts" => await controller.ConfirmOwnAccountsTransfer(Confirmation()),
            "card" => await controller.ConfirmCardPayment(Confirmation()),
            "loan" => await controller.ConfirmLoanPayment(Confirmation()),
            "cash-advance" => await controller.ConfirmCashAdvance(Confirmation()),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(HomeController.Client), redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
    }

    private static ClientOperationConfirmationViewModel Confirmation() => new() {
        ConfirmationToken = "server-issued-confirmation-token",
        SourceAccountNumber = "000000001",
        DestinationAccountNumber = "000000002",
        AccountNumber = "000000001",
        BeneficiaryId = 1,
        CardId = 1,
        LoanId = 1,
        Amount = 10m,
    };
}
