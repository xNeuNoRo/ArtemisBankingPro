extern alias WebApp;

using ArtemisBankingPro.Application.Features.Client.Services;
using ArtemisBankingPro.Application.Features.Client.ViewModels;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebApp::ArtemisBankingPro.WebApp.Controllers;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

public sealed class WebAppClientControllerTests {
    [Fact]
    public async Task Invalid_transaction_filters_are_rejected_before_loading_products() {
        Mock<IClientOperationsService> client = new();
        client.Setup(service => service.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ClientDashboardViewModel()));
        var controller = CreateController(client);

        IActionResult result = await controller.Transactions(
            dateFrom: new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero),
            dateTo: new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero)
        );

        Assert.IsType<ViewResult>(result);
        client.Verify(
            service => service.GetAccountTransactionsAsync(
                It.IsAny<MyAccountTransactionsViewModel>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task Invalid_transaction_type_is_rejected_before_loading_transactions() {
        Mock<IClientOperationsService> client = new();
        client.Setup(service => service.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ClientDashboardViewModel()));
        var controller = CreateController(client);

        IActionResult result = await controller.Transactions(transactionType: "INVALIDO");

        Assert.IsType<ViewResult>(result);
        Assert.Contains(
            controller.ModelState[nameof(MyAccountTransactionsViewModel.TransactionType)]!.Errors,
            error => error.ErrorMessage == "El tipo de transacción debe ser crédito o débito."
        );
        client.Verify(
            service => service.GetAccountTransactionsAsync(
                It.IsAny<MyAccountTransactionsViewModel>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task Invalid_card_detail_id_returns_not_found_before_application_validation() {
        Mock<IClientOperationsService> client = new();
        var controller = CreateController(client);

        IActionResult result = await controller.CardDetails(0);

        Assert.IsType<NotFoundResult>(result);
        client.Verify(
            service => service.GetCardAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task CashAdvanceQuote_keeps_selected_card_id_for_confirmation() {
        Mock<IClientOperationsService> client = new();
        client.Setup(service => service.GetCashAdvanceQuoteAsync(
                It.Is<CashAdvanceQuoteViewModel>(model => model.CardId == 42),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Result.Success(new CashAdvanceQuoteViewModel {
                CardId = 42,
                Amount = 100m,
                PrincipalAmount = 100m,
                InterestAmount = 6.25m,
                TotalToCharge = 106.25m,
                AvailableCredit = 500m,
                IsEligible = true,
            }));
        client.Setup(service => service.IssueCashAdvanceConfirmationAsync(
                It.Is<CashAdvanceViewModel>(model => model.CardId == 42),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Result.Success("confirmation-token"));

        Mock<IConfirmationTokenService> tokens = new();
        tokens.Setup(service => service.ValidateAndConsumeAsync(
                "form-token",
                "client-1",
                It.IsAny<string>(),
                "form",
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(ConfirmationValidationResult.Valid());

        var controller = CreateController(client, tokens.Object);
        controller.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "client-1")],
                "test"
            )
        );

        IActionResult result = await controller.CashAdvanceQuote(
            new CashAdvanceViewModel {
                CardId = 42,
                DestinationAccountNumber = "100000001",
                Amount = 100m,
                SubmissionToken = "form-token",
            }
        );

        var view = Assert.IsType<ViewResult>(result);
        var confirmation = Assert.IsType<ClientOperationConfirmationViewModel>(view.Model);
        Assert.Equal(42, confirmation.CardId);
        Assert.Equal("confirmation-token", confirmation.ConfirmationToken);
    }

    [Fact]
    public async Task Incomplete_confirmation_does_not_dispatch_a_financial_command() {
        Mock<IClientOperationsService> client = new();
        var controller = CreateController(client);

        IActionResult result = await controller.ConfirmExpressTransfer(
            new ClientOperationConfirmationViewModel {
                ConfirmationToken = "server-issued-confirmation-token",
            }
        );

        var view = Assert.IsType<ViewResult>(result);
        var confirmation = Assert.IsType<ClientOperationConfirmationViewModel>(view.Model);
        Assert.Equal(nameof(ClientController.ConfirmExpressTransfer), confirmation.ConfirmAction);
        client.Verify(
            service => service.ExpressTransferAsync(
                It.IsAny<ExpressTransactionViewModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task Invalid_remove_confirmation_does_not_load_beneficiaries() {
        Mock<IClientOperationsService> client = new();
        var controller = CreateController(client);
        controller.ModelState.AddModelError(
            nameof(RemoveBeneficiaryViewModel.ConfirmationToken),
            "El token de confirmación es requerido."
        );

        IActionResult result = await controller.RemoveBeneficiary(
            42,
            new RemoveBeneficiaryViewModel()
        );

        var view = Assert.IsType<ViewResult>(result);
        var confirmation = Assert.IsType<RemoveBeneficiaryViewModel>(view.Model);
        Assert.Equal(42, confirmation.BeneficiaryId);
        client.Verify(
            service => service.GetBeneficiariesAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

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

        var controller = CreateController(client);
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

    private static ClientController CreateController(
        Mock<IClientOperationsService> client,
        IConfirmationTokenService? tokens = null
    ) {
        var controller = new ClientController(
            client.Object,
            tokens ?? Mock.Of<IConfirmationTokenService>(),
            NullLogger<ClientController>.Instance
        );
        controller.ControllerContext = new ControllerContext {
            HttpContext = new DefaultHttpContext(),
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            Mock.Of<ITempDataProvider>()
        );
        return controller;
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
