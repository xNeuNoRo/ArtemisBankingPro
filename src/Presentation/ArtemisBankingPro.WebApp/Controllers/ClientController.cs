using System.Security.Claims;
using System.Globalization;
using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Application.Features.Client.Services;
using ArtemisBankingPro.Application.Features.Client.ViewModels;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.WebApp.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ArtemisBankingPro.WebApp.Controllers;

[Authorize(Roles = nameof(Roles.Cliente))]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[TypeFilter(typeof(ClientAccessExceptionFilterAttribute))]
[Route("client")]
public sealed class ClientController : Controller {
    private const int PageSize = 20;
    private static readonly TimeSpan FormTokenLifetime = TimeSpan.FromMinutes(30);

    private const string AddBeneficiaryFormOperation =
        "WebApp.Client.AddBeneficiary";
    private const string ExpressTransferFormOperation =
        "WebApp.Client.ExpressTransfer";
    private const string BeneficiaryTransferFormOperation =
        "WebApp.Client.BeneficiaryTransfer";
    private const string OwnAccountsTransferFormOperation =
        "WebApp.Client.OwnAccountsTransfer";
    private const string CardPaymentFormOperation =
        "WebApp.Client.CardPayment";
    private const string LoanPaymentFormOperation =
        "WebApp.Client.LoanPayment";
    private const string CashAdvanceFormOperation =
        "WebApp.Client.CashAdvance";

    private readonly IClientOperationsService _client;
    private readonly IConfirmationTokenService _tokens;
    private readonly ILogger<ClientController> _logger;

    public ClientController(
        IClientOperationsService client,
        IConfirmationTokenService tokens,
        ILogger<ClientController> logger
    ) {
        _client = client;
        _tokens = tokens;
        _logger = logger;
    }

    [HttpGet("")]
    public IActionResult Index() => RedirectToAction(nameof(HomeController.Client), "Home");

    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions(
        string? accountNumber = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        string? transactionType = null,
        int page = 1,
        CancellationToken cancellationToken = default
    ) {
        Result<ClientDashboardViewModel> dashboard = await _client.GetDashboardAsync(cancellationToken);
        if (dashboard.IsFailure) {
            return HandleReadFailure(dashboard.Error);
        }

        IReadOnlyList<SelectOptionViewModel> accountOptions = AccountOptions(dashboard.Value.Products);
        string selectedAccount = Normalize(accountNumber)
            ?? (dashboard.Value.Products.Accounts.Count > 0
                ? dashboard.Value.Products.Accounts[0].AccountNumber
                : null)
            ?? string.Empty;
        MyAccountTransactionsViewModel request = new() {
            AccountNumber = selectedAccount,
            DateFrom = dateFrom,
            DateTo = dateTo,
            TransactionType = Normalize(transactionType),
        };

        if (string.IsNullOrWhiteSpace(selectedAccount)) {
            return View(DecorateTransactions(request, accountOptions, []));
        }

        if (!TryValidateModel(request)) {
            return View(DecorateTransactions(request, accountOptions, []));
        }

        Result<MyAccountTransactionsViewModel> result =
            await _client.GetAccountTransactionsAsync(
                request,
                Math.Max(1, page),
                PageSize,
                cancellationToken
            );
        return result.IsFailure
            ? HandleReadFailure(result.Error)
            : View(DecorateTransactions(result.Value, accountOptions, result.Value.Transactions));
    }

    [HttpGet("loans/{id:int}")]
    public async Task<IActionResult> LoanDetails(
        int id,
        CancellationToken cancellationToken = default
    ) {
        Result<MyLoanDetailViewModel> result = await _client.GetLoanAsync(id, cancellationToken);
        return result.IsFailure
            ? HandleReadFailure(result.Error)
            : View(DecorateLoan(result.Value));
    }

    [HttpGet("cards/{id:int}")]
    public async Task<IActionResult> CardDetails(
        int id,
        int page = 1,
        CancellationToken cancellationToken = default
    ) {
        Result<MyCardDetailViewModel> result = await _client.GetCardAsync(
            id,
            Math.Max(1, page),
            PageSize,
            cancellationToken
        );
        return result.IsFailure
            ? HandleReadFailure(result.Error)
            : View(DecorateCard(result.Value));
    }

    [HttpGet("beneficiaries")]
    public Task<IActionResult> Beneficiaries(
        bool add = false,
        CancellationToken cancellationToken = default
    ) => RenderBeneficiariesAsync(new AddBeneficiaryViewModel(), add, cancellationToken);

    [HttpPost("beneficiaries/add")]
    public async Task<IActionResult> AddBeneficiary(
        [Bind(Prefix = "AddForm")] AddBeneficiaryViewModel model,
        CancellationToken cancellationToken = default
    ) {
        if (!ModelState.IsValid) {
            return await RenderBeneficiariesAsync(model, false, cancellationToken);
        }

        if (!await ValidateFormTokenAsync(
                model.SubmissionToken,
                AddBeneficiaryFormOperation,
                cancellationToken
            )) {
            AddFormTokenError();
            return await RenderBeneficiariesAsync(model, true, cancellationToken);
        }

        Result<string> confirmation = await _client.IssueAddBeneficiaryConfirmationAsync(
            model,
            cancellationToken
        );
        if (confirmation.IsFailure) {
            AddResultError(confirmation.Error, "No fue posible preparar la confirmación.");
            return await RenderBeneficiariesAsync(model, true, cancellationToken);
        }

        return View(
            "ConfirmOperation",
            BuildConfirmation(
                confirmation.Value,
                "Agregar beneficiario",
                "¿Está seguro que desea registrar esta cuenta como beneficiario?",
                nameof(ConfirmAddBeneficiary),
                destinationAccountNumber: model.DestinationAccountNumber
            )
        );
    }

    [HttpPost("beneficiaries/add/confirm")]
    public async Task<IActionResult> ConfirmAddBeneficiary(
        ClientOperationConfirmationViewModel model,
        CancellationToken cancellationToken = default
    ) {
        if (!ModelState.IsValid) {
            return View("ConfirmOperation", model);
        }

        AddBeneficiaryViewModel form = new() {
            DestinationAccountNumber = model.DestinationAccountNumber ?? string.Empty,
            SubmissionToken = model.ConfirmationToken,
        };
        Result result = await _client.AddBeneficiaryAsync(
            form,
            model.ConfirmationToken,
            cancellationToken
        );
        if (result.IsSuccess) {
            TempData["Success"] = "Beneficiario agregado correctamente.";
            return RedirectToAction(nameof(Beneficiaries));
        }

        IActionResult? accessResult = HandleMutationFailure(result.Error, "No fue posible agregar el beneficiario.");
        if (accessResult is not null) {
            return accessResult;
        }

        Result<string> replacement = await _client.IssueAddBeneficiaryConfirmationAsync(
            form,
            cancellationToken
        );
        return replacement.IsFailure
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : View(
                "ConfirmOperation",
                BuildConfirmation(
                    replacement.Value,
                    "Agregar beneficiario",
                    "¿Está seguro que desea registrar esta cuenta como beneficiario?",
                    nameof(ConfirmAddBeneficiary),
                    destinationAccountNumber: form.DestinationAccountNumber
                )
            );
    }

    [HttpGet("beneficiaries/remove/{id:int}")]
    public async Task<IActionResult> RemoveBeneficiary(
        int id,
        CancellationToken cancellationToken = default
    ) {
        Result<BeneficiaryListViewModel> result = await _client.GetBeneficiariesAsync(cancellationToken);
        if (result.IsFailure) {
            return HandleReadFailure(result.Error);
        }

        BeneficiaryItemViewModel? beneficiary = result.Value.Beneficiaries
            .SingleOrDefault(item => item.BeneficiaryId == id);
        if (beneficiary is null) {
            return NotFound();
        }

        Result<string> token = await _client.IssueRemoveBeneficiaryConfirmationAsync(
            id,
            cancellationToken
        );
        return token.IsFailure
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : View(new RemoveBeneficiaryViewModel {
                BeneficiaryId = beneficiary.BeneficiaryId,
                BeneficiaryName = $"{beneficiary.FirstName} {beneficiary.LastName}".Trim(),
                AccountNumber = beneficiary.AccountNumber,
                ConfirmationToken = token.Value,
                PageTitle = "Eliminar beneficiario",
                Title = "Eliminar beneficiario",
                Message = "¿Está seguro que desea eliminar este beneficiario?",
                ConfirmButtonText = "Aceptar",
                CancelButtonText = "Cancelar",
                CurrentUserId = CurrentUserId,
                CurrentUserName = CurrentUserName,
                CurrentUserRole = nameof(Roles.Cliente),
                IsAuthenticated = true,
                ActiveNavigationItem = NavigationKeys.ClientBeneficiaries,
            });
    }

    [HttpPost("beneficiaries/remove/{id:int}")]
    public async Task<IActionResult> RemoveBeneficiary(
        int id,
        RemoveBeneficiaryViewModel model,
        CancellationToken cancellationToken = default
    ) {
        Result<BeneficiaryListViewModel> list = await _client.GetBeneficiariesAsync(cancellationToken);
        if (list.IsFailure) {
            return HandleReadFailure(list.Error);
        }

        BeneficiaryItemViewModel? beneficiary = list.Value.Beneficiaries
            .SingleOrDefault(item => item.BeneficiaryId == id);
        if (beneficiary is null) {
            return NotFound();
        }

        if (!ModelState.IsValid) {
            return View(BuildRemoveModel(beneficiary, model.ConfirmationToken));
        }

        model = new RemoveBeneficiaryViewModel {
            BeneficiaryId = id,
            BeneficiaryName = $"{beneficiary.FirstName} {beneficiary.LastName}".Trim(),
            AccountNumber = beneficiary.AccountNumber,
            ConfirmationToken = model.ConfirmationToken,
        };
        Result result = await _client.RemoveBeneficiaryAsync(
            id,
            model,
            model.ConfirmationToken,
            cancellationToken
        );
        if (result.IsSuccess) {
            TempData["Success"] = "Beneficiario eliminado correctamente.";
            return RedirectToAction(nameof(Beneficiaries));
        }

        IActionResult? accessResult = HandleMutationFailure(result.Error, "No fue posible eliminar el beneficiario.");
        if (accessResult is not null) {
            return accessResult;
        }

        Result<string> replacement = await _client.IssueRemoveBeneficiaryConfirmationAsync(
            id,
            cancellationToken
        );
        return replacement.IsFailure
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : View(BuildRemoveModel(beneficiary, replacement.Value));
    }

    [HttpGet("transfer-express")]
    public Task<IActionResult> ExpressTransfer(CancellationToken cancellationToken = default) =>
        RenderExpressTransferAsync(new ExpressTransactionViewModel(), true, cancellationToken);

    [HttpPost("transfer-express")]
    public async Task<IActionResult> ExpressTransfer(
        [Bind(Prefix = "Form")] ExpressTransactionViewModel model,
        CancellationToken cancellationToken = default
    ) {
        if (!ModelState.IsValid) {
            return await RenderExpressTransferAsync(model, false, cancellationToken);
        }

        if (!await ValidateFormTokenAsync(model.SubmissionToken, ExpressTransferFormOperation, cancellationToken)) {
            AddFormTokenError();
            return await RenderExpressTransferAsync(model, true, cancellationToken);
        }

        Result<ClientTransferTargetViewModel> target = await _client.GetExpressTransferTargetAsync(
            model.DestinationAccountNumber,
            cancellationToken
        );
        if (target.IsFailure) {
            AddResultError(target.Error, "El número de cuenta ingresado no corresponde a una cuenta válida.");
            return await RenderExpressTransferAsync(model, true, cancellationToken);
        }

        Result<string> confirmation = await _client.IssueExpressTransferConfirmationAsync(
            model,
            cancellationToken
        );
        if (confirmation.IsFailure) {
            AddResultError(confirmation.Error, "No fue posible preparar la confirmación.");
            return await RenderExpressTransferAsync(model, true, cancellationToken);
        }

        return View(
            "ConfirmOperation",
            BuildConfirmation(
                confirmation.Value,
                "Transferencia express",
                "¿Está seguro de que desea realizar esta transacción?",
                nameof(ConfirmExpressTransfer),
                target.Value.FullName,
                model.SourceAccountNumber,
                model.DestinationAccountNumber,
                amount: model.Amount
            )
        );
    }

    [HttpPost("transfer-express/confirm")]
    public async Task<IActionResult> ConfirmExpressTransfer(
        ClientOperationConfirmationViewModel model,
        CancellationToken cancellationToken = default
    ) {
        if (!ModelState.IsValid) {
            return View("ConfirmOperation", model);
        }

        ExpressTransactionViewModel form = new() {
            SourceAccountNumber = model.SourceAccountNumber ?? string.Empty,
            DestinationAccountNumber = model.DestinationAccountNumber ?? string.Empty,
            Amount = model.Amount,
            SubmissionToken = model.ConfirmationToken,
        };
        Result result = await _client.ExpressTransferAsync(
            form,
            model.ConfirmationToken,
            cancellationToken
        );
        return await FinishFinancialMutationAsync(
            result,
            "La transacción fue realizada correctamente.",
            nameof(HomeController.Client),
            null,
            () => _client.IssueExpressTransferConfirmationAsync(form, cancellationToken),
            () => BuildConfirmation(
                "",
                "Transferencia express",
                "¿Está seguro de que desea realizar esta transacción?",
                nameof(ConfirmExpressTransfer),
                sourceAccountNumber: form.SourceAccountNumber,
                destinationAccountNumber: form.DestinationAccountNumber,
                amount: form.Amount
            )
        );
    }

    [HttpGet("transfer-beneficiary")]
    public Task<IActionResult> BeneficiaryTransfer(CancellationToken cancellationToken = default) =>
        RenderBeneficiaryTransferAsync(new BeneficiaryTransferViewModel(), true, cancellationToken);

    [HttpPost("transfer-beneficiary")]
    public async Task<IActionResult> BeneficiaryTransfer(
        [Bind(Prefix = "Form")] BeneficiaryTransferViewModel model,
        CancellationToken cancellationToken = default
    ) {
        if (!ModelState.IsValid) {
            return await RenderBeneficiaryTransferAsync(model, false, cancellationToken);
        }

        if (!await ValidateFormTokenAsync(model.SubmissionToken, BeneficiaryTransferFormOperation, cancellationToken)) {
            AddFormTokenError();
            return await RenderBeneficiaryTransferAsync(model, true, cancellationToken);
        }

        Result<BeneficiaryListViewModel> beneficiaries = await _client.GetBeneficiariesAsync(cancellationToken);
        if (beneficiaries.IsFailure) {
            return HandleReadFailure(beneficiaries.Error);
        }

        BeneficiaryItemViewModel? target = beneficiaries.Value.Beneficiaries
            .SingleOrDefault(item => item.BeneficiaryId == model.BeneficiaryId);
        if (target is null) {
            AddResultError(null, "La cuenta del beneficiario no se encuentra disponible.");
            return await RenderBeneficiaryTransferAsync(model, true, cancellationToken);
        }

        Result<string> confirmation = await _client.IssueBeneficiaryTransferConfirmationAsync(
            model,
            cancellationToken
        );
        if (confirmation.IsFailure) {
            AddResultError(confirmation.Error, "No fue posible preparar la confirmación.");
            return await RenderBeneficiaryTransferAsync(model, true, cancellationToken);
        }

        return View(
            "ConfirmOperation",
            BuildConfirmation(
                confirmation.Value,
                "Transferencia a beneficiario",
                "¿Está seguro de que desea realizar esta transacción?",
                nameof(ConfirmBeneficiaryTransfer),
                $"{target.FirstName} {target.LastName}".Trim(),
                model.SourceAccountNumber,
                target.AccountNumber,
                model.BeneficiaryId,
                amount: model.Amount
            )
        );
    }

    [HttpPost("transfer-beneficiary/confirm")]
    public async Task<IActionResult> ConfirmBeneficiaryTransfer(
        ClientOperationConfirmationViewModel model,
        CancellationToken cancellationToken = default
    ) {
        if (!ModelState.IsValid) {
            return View("ConfirmOperation", model);
        }

        BeneficiaryTransferViewModel form = new() {
            BeneficiaryId = model.BeneficiaryId ?? 0,
            SourceAccountNumber = model.SourceAccountNumber ?? string.Empty,
            Amount = model.Amount,
            SubmissionToken = model.ConfirmationToken,
        };
        Result result = await _client.BeneficiaryTransferAsync(
            form,
            model.ConfirmationToken,
            cancellationToken
        );
        return await FinishFinancialMutationAsync(
            result,
            "La transacción fue realizada correctamente.",
            nameof(HomeController.Client),
            null,
            () => _client.IssueBeneficiaryTransferConfirmationAsync(form, cancellationToken),
            () => BuildConfirmation(
                "",
                "Transferencia a beneficiario",
                "¿Está seguro de que desea realizar esta transacción?",
                nameof(ConfirmBeneficiaryTransfer),
                model.TargetName,
                sourceAccountNumber: form.SourceAccountNumber,
                destinationAccountNumber: model.DestinationAccountNumber,
                beneficiaryId: form.BeneficiaryId,
                amount: form.Amount
            )
        );
    }

    [HttpGet("transfer-own-accounts")]
    public Task<IActionResult> OwnAccountsTransfer(CancellationToken cancellationToken = default) =>
        RenderOwnAccountsTransferAsync(new OwnAccountsTransferViewModel(), true, cancellationToken);

    [HttpPost("transfer-own-accounts")]
    public async Task<IActionResult> OwnAccountsTransfer(
        [Bind(Prefix = "Form")] OwnAccountsTransferViewModel model,
        CancellationToken cancellationToken = default
    ) {
        if (!ModelState.IsValid) {
            return await RenderOwnAccountsTransferAsync(model, false, cancellationToken);
        }

        if (!await ValidateFormTokenAsync(model.SubmissionToken, OwnAccountsTransferFormOperation, cancellationToken)) {
            AddFormTokenError();
            return await RenderOwnAccountsTransferAsync(model, true, cancellationToken);
        }

        Result<string> confirmation = await _client.IssueOwnAccountsTransferConfirmationAsync(
            model,
            cancellationToken
        );
        if (confirmation.IsFailure) {
            AddResultError(confirmation.Error, "No fue posible preparar la confirmación.");
            return await RenderOwnAccountsTransferAsync(model, true, cancellationToken);
        }

        return View(
            "ConfirmOperation",
            BuildConfirmation(
                confirmation.Value,
                "Transferencia entre cuentas propias",
                "¿Está seguro de que desea realizar esta transferencia?",
                nameof(ConfirmOwnAccountsTransfer),
                sourceAccountNumber: model.SourceAccountNumber,
                destinationAccountNumber: model.DestinationAccountNumber,
                amount: model.Amount
            )
        );
    }

    [HttpPost("transfer-own-accounts/confirm")]
    public async Task<IActionResult> ConfirmOwnAccountsTransfer(
        ClientOperationConfirmationViewModel model,
        CancellationToken cancellationToken = default
    ) {
        if (!ModelState.IsValid) {
            return View("ConfirmOperation", model);
        }

        OwnAccountsTransferViewModel form = new() {
            SourceAccountNumber = model.SourceAccountNumber ?? string.Empty,
            DestinationAccountNumber = model.DestinationAccountNumber ?? string.Empty,
            Amount = model.Amount,
            SubmissionToken = model.ConfirmationToken,
        };
        Result result = await _client.OwnAccountsTransferAsync(
            form,
            model.ConfirmationToken,
            cancellationToken
        );
        return await FinishFinancialMutationAsync(
            result,
            "La transferencia fue realizada correctamente.",
            nameof(HomeController.Client),
            null,
            () => _client.IssueOwnAccountsTransferConfirmationAsync(form, cancellationToken),
            () => BuildConfirmation(
                "",
                "Transferencia entre cuentas propias",
                "¿Está seguro de que desea realizar esta transferencia?",
                nameof(ConfirmOwnAccountsTransfer),
                sourceAccountNumber: form.SourceAccountNumber,
                destinationAccountNumber: form.DestinationAccountNumber,
                amount: form.Amount
            )
        );
    }

    [HttpGet("card-payment")]
    public Task<IActionResult> CardPayment(CancellationToken cancellationToken = default) =>
        RenderCardPaymentAsync(new ClientCardPaymentViewModel(), true, cancellationToken);

    [HttpPost("card-payment")]
    public async Task<IActionResult> CardPayment(
        [Bind(Prefix = "Form")] ClientCardPaymentViewModel model,
        CancellationToken cancellationToken = default
    ) {
        if (!ModelState.IsValid) {
            return await RenderCardPaymentAsync(model, false, cancellationToken);
        }

        if (!await ValidateFormTokenAsync(model.SubmissionToken, CardPaymentFormOperation, cancellationToken)) {
            AddFormTokenError();
            return await RenderCardPaymentAsync(model, true, cancellationToken);
        }

        Result<ClientDashboardViewModel> dashboard = await _client.GetDashboardAsync(cancellationToken);
        if (dashboard.IsFailure) {
            return HandleReadFailure(dashboard.Error);
        }

        MyCardViewModel? card = dashboard.Value.Products.Cards
            .SingleOrDefault(item => item.CardId == model.CardId);
        if (card is null) {
            AddResultError(null, "La tarjeta seleccionada no se encuentra disponible.");
            return await RenderCardPaymentAsync(model, true, cancellationToken);
        }

        Result<string> confirmation = await _client.IssueCardPaymentConfirmationAsync(model, cancellationToken);
        if (confirmation.IsFailure) {
            AddResultError(confirmation.Error, "No fue posible preparar la confirmación.");
            return await RenderCardPaymentAsync(model, true, cancellationToken);
        }

        return View(
            "ConfirmOperation",
            BuildConfirmation(
                confirmation.Value,
                "Pago de tarjeta de crédito",
                "¿Está seguro de que desea realizar este pago?",
                nameof(ConfirmCardPayment),
                $"Tarjeta terminada en {card.LastFour}",
                accountNumber: model.AccountNumber,
                cardId: model.CardId,
                amount: model.Amount
            )
        );
    }

    [HttpPost("card-payment/confirm")]
    public async Task<IActionResult> ConfirmCardPayment(
        ClientOperationConfirmationViewModel model,
        CancellationToken cancellationToken = default
    ) {
        if (!ModelState.IsValid) {
            return View("ConfirmOperation", model);
        }

        ClientCardPaymentViewModel form = new() {
            CardId = model.CardId ?? 0,
            AccountNumber = model.AccountNumber ?? string.Empty,
            Amount = model.Amount,
            SubmissionToken = model.ConfirmationToken,
        };
        Result result = await _client.PayCardAsync(form, model.ConfirmationToken, cancellationToken);
        return await FinishFinancialMutationAsync(
            result,
            "El pago a la tarjeta fue realizado correctamente.",
            nameof(HomeController.Client),
            null,
            () => _client.IssueCardPaymentConfirmationAsync(form, cancellationToken),
            () => BuildConfirmation(
                "",
                "Pago de tarjeta de crédito",
                "¿Está seguro de que desea realizar este pago?",
                nameof(ConfirmCardPayment),
                model.TargetName,
                accountNumber: form.AccountNumber,
                cardId: form.CardId,
                amount: form.Amount
            )
        );
    }

    [HttpGet("loan-payment")]
    public Task<IActionResult> LoanPayment(CancellationToken cancellationToken = default) =>
        RenderLoanPaymentAsync(new ClientLoanPaymentViewModel(), true, cancellationToken);

    [HttpPost("loan-payment")]
    public async Task<IActionResult> LoanPayment(
        [Bind(Prefix = "Form")] ClientLoanPaymentViewModel model,
        CancellationToken cancellationToken = default
    ) {
        if (!ModelState.IsValid) {
            return await RenderLoanPaymentAsync(model, false, cancellationToken);
        }

        if (!await ValidateFormTokenAsync(model.SubmissionToken, LoanPaymentFormOperation, cancellationToken)) {
            AddFormTokenError();
            return await RenderLoanPaymentAsync(model, true, cancellationToken);
        }

        Result<ClientDashboardViewModel> dashboard = await _client.GetDashboardAsync(cancellationToken);
        if (dashboard.IsFailure) {
            return HandleReadFailure(dashboard.Error);
        }

        MyLoanViewModel? loan = dashboard.Value.Products.Loans
            .SingleOrDefault(item => item.LoanId == model.LoanId);
        if (loan is null) {
            AddResultError(null, "El préstamo seleccionado no se encuentra disponible.");
            return await RenderLoanPaymentAsync(model, true, cancellationToken);
        }

        Result<string> confirmation = await _client.IssueLoanPaymentConfirmationAsync(model, cancellationToken);
        if (confirmation.IsFailure) {
            AddResultError(confirmation.Error, "No fue posible preparar la confirmación.");
            return await RenderLoanPaymentAsync(model, true, cancellationToken);
        }

        return View(
            "ConfirmOperation",
            BuildConfirmation(
                confirmation.Value,
                "Pago de préstamo",
                "¿Está seguro de que desea realizar este pago?",
                nameof(ConfirmLoanPayment),
                $"Préstamo {loan.LoanNumber}",
                accountNumber: model.AccountNumber,
                loanId: model.LoanId,
                amount: model.Amount
            )
        );
    }

    [HttpPost("loan-payment/confirm")]
    public async Task<IActionResult> ConfirmLoanPayment(
        ClientOperationConfirmationViewModel model,
        CancellationToken cancellationToken = default
    ) {
        if (!ModelState.IsValid) {
            return View("ConfirmOperation", model);
        }

        ClientLoanPaymentViewModel form = new() {
            LoanId = model.LoanId ?? 0,
            AccountNumber = model.AccountNumber ?? string.Empty,
            Amount = model.Amount,
            SubmissionToken = model.ConfirmationToken,
        };
        Result result = await _client.PayLoanAsync(form, model.ConfirmationToken, cancellationToken);
        return await FinishFinancialMutationAsync(
            result,
            "El pago al préstamo fue realizado correctamente.",
            nameof(HomeController.Client),
            null,
            () => _client.IssueLoanPaymentConfirmationAsync(form, cancellationToken),
            () => BuildConfirmation(
                "",
                "Pago de préstamo",
                "¿Está seguro de que desea realizar este pago?",
                nameof(ConfirmLoanPayment),
                model.TargetName,
                accountNumber: form.AccountNumber,
                loanId: form.LoanId,
                amount: form.Amount
            )
        );
    }

    [HttpGet("cash-advance")]
    public Task<IActionResult> CashAdvance(CancellationToken cancellationToken = default) =>
        RenderCashAdvanceAsync(new CashAdvanceViewModel(), true, cancellationToken);

    [HttpPost("cash-advance/quote")]
    public async Task<IActionResult> CashAdvanceQuote(
        [Bind(Prefix = "Form")] CashAdvanceViewModel model,
        CancellationToken cancellationToken = default
    ) {
        if (!ModelState.IsValid) {
            return await RenderCashAdvanceAsync(model, false, cancellationToken);
        }

        if (!await ValidateFormTokenAsync(model.SubmissionToken, CashAdvanceFormOperation, cancellationToken)) {
            AddFormTokenError();
            return await RenderCashAdvanceAsync(model, true, cancellationToken);
        }

        CashAdvanceQuoteViewModel quoteRequest = new() {
            CardId = model.CardId,
            Amount = model.Amount,
        };
        Result<CashAdvanceQuoteViewModel> quote = await _client.GetCashAdvanceQuoteAsync(
            quoteRequest,
            cancellationToken
        );
        if (quote.IsFailure) {
            AddResultError(quote.Error, "No fue posible calcular el avance solicitado.");
            return await RenderCashAdvanceAsync(model, true, cancellationToken);
        }

        if (!quote.Value.IsEligible) {
            AddResultError(
                null,
                "El avance solicitado excede el crédito disponible de la tarjeta seleccionada."
            );
            return await RenderCashAdvanceAsync(model, true, cancellationToken, quote.Value);
        }

        model.Quote = quote.Value;
        Result<string> confirmation = await _client.IssueCashAdvanceConfirmationAsync(
            model,
            cancellationToken
        );
        if (confirmation.IsFailure) {
            AddResultError(confirmation.Error, "No fue posible preparar la confirmación.");
            return await RenderCashAdvanceAsync(model, true, cancellationToken, quote.Value);
        }

        return View(
            "ConfirmOperation",
            BuildConfirmation(
                confirmation.Value,
                "Avance de efectivo",
                "¿Está seguro de que desea realizar este avance?",
                nameof(ConfirmCashAdvance),
                amount: model.Amount,
                cardId: model.CardId,
                destinationAccountNumber: model.DestinationAccountNumber,
                interestAmount: quote.Value.InterestAmount,
                totalToCharge: quote.Value.TotalToCharge
            )
        );
    }

    [HttpPost("cash-advance/confirm")]
    public async Task<IActionResult> ConfirmCashAdvance(
        ClientOperationConfirmationViewModel model,
        CancellationToken cancellationToken = default
    ) {
        if (!ModelState.IsValid) {
            return View("ConfirmOperation", model);
        }

        CashAdvanceViewModel form = new() {
            CardId = model.CardId ?? 0,
            DestinationAccountNumber = model.DestinationAccountNumber ?? string.Empty,
            Amount = model.Amount,
            SubmissionToken = model.ConfirmationToken,
        };
        Result result = await _client.CashAdvanceAsync(form, model.ConfirmationToken, cancellationToken);
        return await FinishFinancialMutationAsync(
            result,
            "El avance fue realizado correctamente.",
            nameof(HomeController.Client),
            null,
            () => _client.IssueCashAdvanceConfirmationAsync(form, cancellationToken),
            () => BuildConfirmation(
                "",
                "Avance de efectivo",
                "¿Está seguro de que desea realizar este avance?",
                nameof(ConfirmCashAdvance),
                amount: form.Amount,
                cardId: form.CardId,
                destinationAccountNumber: form.DestinationAccountNumber,
                interestAmount: model.InterestAmount,
                totalToCharge: model.TotalToCharge
            )
        );
    }

    private async Task<IActionResult> RenderBeneficiariesAsync(
        AddBeneficiaryViewModel form,
        bool issueNewToken,
        CancellationToken cancellationToken
    ) {
        Result<BeneficiaryListViewModel> result = await _client.GetBeneficiariesAsync(cancellationToken);
        if (result.IsFailure) {
            return HandleReadFailure(result.Error);
        }

        bool showAddForm = issueNewToken || ModelState.ErrorCount > 0;
        string? token = showAddForm && (issueNewToken || string.IsNullOrWhiteSpace(form.SubmissionToken))
            ? await IssueFormTokenAsync(AddBeneficiaryFormOperation, cancellationToken)
            : form.SubmissionToken;
        if (showAddForm && token is null) {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return View(
            "Beneficiaries",
            new ClientBeneficiariesPageViewModel {
                Beneficiaries = result.Value.Beneficiaries,
                AddForm = new AddBeneficiaryViewModel {
                    DestinationAccountNumber = form.DestinationAccountNumber,
                    SubmissionToken = token ?? string.Empty,
                },
                PageTitle = "Beneficiarios",
                CurrentUserId = CurrentUserId,
                CurrentUserName = CurrentUserName,
                CurrentUserRole = nameof(Roles.Cliente),
                IsAuthenticated = true,
                ActiveNavigationItem = NavigationKeys.ClientBeneficiaries,
                ShowAddForm = showAddForm,
            }
        );
    }

    private async Task<IActionResult> RenderExpressTransferAsync(
        ExpressTransactionViewModel form,
        bool issueNewToken,
        CancellationToken cancellationToken
    ) {
        Result<ClientDashboardViewModel> dashboard = await _client.GetDashboardAsync(cancellationToken);
        if (dashboard.IsFailure) {
            return HandleReadFailure(dashboard.Error);
        }

        SelectOptionViewModel[] accounts = AccountOptions(dashboard.Value.Products);
        string? token = await PrepareFormTokenAsync(
            form.SubmissionToken,
            issueNewToken,
            accounts.Length > 0,
            ExpressTransferFormOperation,
            cancellationToken
        );
        return token is null && accounts.Length > 0
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : View(
                "ExpressTransfer",
                OperationPage(
                    new ExpressTransactionViewModel {
                        SourceAccountNumber = form.SourceAccountNumber,
                        DestinationAccountNumber = form.DestinationAccountNumber,
                        Amount = form.Amount,
                        SourceAccountOptions = accounts,
                        SubmissionToken = token ?? string.Empty,
                    },
                    "Transferencia express",
                    NavigationKeys.ClientExpressTransfer
                )
            );
    }

    private async Task<IActionResult> RenderBeneficiaryTransferAsync(
        BeneficiaryTransferViewModel form,
        bool issueNewToken,
        CancellationToken cancellationToken
    ) {
        Result<ClientDashboardViewModel> dashboard = await _client.GetDashboardAsync(cancellationToken);
        if (dashboard.IsFailure) {
            return HandleReadFailure(dashboard.Error);
        }

        Result<BeneficiaryListViewModel> beneficiaries = await _client.GetBeneficiariesAsync(cancellationToken);
        if (beneficiaries.IsFailure) {
            return HandleReadFailure(beneficiaries.Error);
        }

        SelectOptionViewModel[] accounts = AccountOptions(dashboard.Value.Products);
        SelectOptionViewModel[] beneficiaryOptions = BeneficiaryOptions(beneficiaries.Value.Beneficiaries);
        bool canSubmit = accounts.Length > 0 && beneficiaryOptions.Length > 0;
        string? token = await PrepareFormTokenAsync(
            form.SubmissionToken,
            issueNewToken,
            canSubmit,
            BeneficiaryTransferFormOperation,
            cancellationToken
        );
        return token is null && canSubmit
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : View(
                "BeneficiaryTransfer",
                OperationPage(
                    new BeneficiaryTransferViewModel {
                        BeneficiaryId = form.BeneficiaryId,
                        SourceAccountNumber = form.SourceAccountNumber,
                        Amount = form.Amount,
                        BeneficiaryOptions = beneficiaryOptions,
                        SourceAccountOptions = accounts,
                        SubmissionToken = token ?? string.Empty,
                    },
                    "Transferencia a beneficiario",
                    NavigationKeys.ClientBeneficiaryTransfer
                )
            );
    }

    private async Task<IActionResult> RenderOwnAccountsTransferAsync(
        OwnAccountsTransferViewModel form,
        bool issueNewToken,
        CancellationToken cancellationToken
    ) {
        Result<ClientDashboardViewModel> dashboard = await _client.GetDashboardAsync(cancellationToken);
        if (dashboard.IsFailure) {
            return HandleReadFailure(dashboard.Error);
        }

        SelectOptionViewModel[] accounts = AccountOptions(dashboard.Value.Products);
        string? token = await PrepareFormTokenAsync(
            form.SubmissionToken,
            issueNewToken,
            accounts.Length > 1,
            OwnAccountsTransferFormOperation,
            cancellationToken
        );
        return token is null && accounts.Length > 1
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : View(
                "OwnAccountsTransfer",
                OperationPage(
                    new OwnAccountsTransferViewModel {
                        SourceAccountNumber = form.SourceAccountNumber,
                        DestinationAccountNumber = form.DestinationAccountNumber,
                        Amount = form.Amount,
                        AccountOptions = accounts,
                        SubmissionToken = token ?? string.Empty,
                    },
                    "Transferencia entre cuentas propias",
                    NavigationKeys.ClientOwnAccountsTransfer
                )
            );
    }

    private async Task<IActionResult> RenderCardPaymentAsync(
        ClientCardPaymentViewModel form,
        bool issueNewToken,
        CancellationToken cancellationToken
    ) {
        Result<ClientDashboardViewModel> dashboard = await _client.GetDashboardAsync(cancellationToken);
        if (dashboard.IsFailure) {
            return HandleReadFailure(dashboard.Error);
        }

        SelectOptionViewModel[] accounts = AccountOptions(dashboard.Value.Products);
        SelectOptionViewModel[] cards = CardOptions(dashboard.Value.Products);
        bool canSubmit = accounts.Length > 0 && cards.Length > 0;
        string? token = await PrepareFormTokenAsync(
            form.SubmissionToken,
            issueNewToken,
            canSubmit,
            CardPaymentFormOperation,
            cancellationToken
        );
        return token is null && canSubmit
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : View(
                "CardPayment",
                OperationPage(
                    new ClientCardPaymentViewModel {
                        CardId = form.CardId,
                        AccountNumber = form.AccountNumber,
                        Amount = form.Amount,
                        CardOptions = cards,
                        AccountOptions = accounts,
                        SubmissionToken = token ?? string.Empty,
                    },
                    "Pago de tarjeta de crédito",
                    NavigationKeys.ClientCardPayment
                )
            );
    }

    private async Task<IActionResult> RenderLoanPaymentAsync(
        ClientLoanPaymentViewModel form,
        bool issueNewToken,
        CancellationToken cancellationToken
    ) {
        Result<ClientDashboardViewModel> dashboard = await _client.GetDashboardAsync(cancellationToken);
        if (dashboard.IsFailure) {
            return HandleReadFailure(dashboard.Error);
        }

        SelectOptionViewModel[] accounts = AccountOptions(dashboard.Value.Products);
        SelectOptionViewModel[] loans = LoanOptions(dashboard.Value.Products);
        bool canSubmit = accounts.Length > 0 && loans.Length > 0;
        string? token = await PrepareFormTokenAsync(
            form.SubmissionToken,
            issueNewToken,
            canSubmit,
            LoanPaymentFormOperation,
            cancellationToken
        );
        return token is null && canSubmit
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : View(
                "LoanPayment",
                OperationPage(
                    new ClientLoanPaymentViewModel {
                        LoanId = form.LoanId,
                        AccountNumber = form.AccountNumber,
                        Amount = form.Amount,
                        LoanOptions = loans,
                        AccountOptions = accounts,
                        SubmissionToken = token ?? string.Empty,
                    },
                    "Pago de préstamo",
                    NavigationKeys.ClientLoanPayment
                )
            );
    }

    private async Task<IActionResult> RenderCashAdvanceAsync(
        CashAdvanceViewModel form,
        bool issueNewToken,
        CancellationToken cancellationToken,
        CashAdvanceQuoteViewModel? quote = null
    ) {
        Result<ClientDashboardViewModel> dashboard = await _client.GetDashboardAsync(cancellationToken);
        if (dashboard.IsFailure) {
            return HandleReadFailure(dashboard.Error);
        }

        SelectOptionViewModel[] accounts = AccountOptions(dashboard.Value.Products);
        SelectOptionViewModel[] cards = CardOptions(dashboard.Value.Products);
        bool canSubmit = accounts.Length > 0 && cards.Length > 0;
        string? token = await PrepareFormTokenAsync(
            form.SubmissionToken,
            issueNewToken,
            canSubmit,
            CashAdvanceFormOperation,
            cancellationToken
        );
        return token is null && canSubmit
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : View(
                "CashAdvance",
                OperationPage(
                    new CashAdvanceViewModel {
                        CardId = form.CardId,
                        DestinationAccountNumber = form.DestinationAccountNumber,
                        Amount = form.Amount,
                        CardOptions = cards,
                        DestinationAccountOptions = accounts,
                        Quote = quote ?? form.Quote,
                        SubmissionToken = token ?? string.Empty,
                    },
                    "Avance de efectivo",
                    NavigationKeys.ClientCashAdvance
                )
            );
    }

    private async Task<IActionResult> FinishFinancialMutationAsync(
        Result result,
        string successMessage,
        string redirectAction,
        object? routeValues,
        Func<Task<Result<string>>> issueReplacement,
        Func<ClientOperationConfirmationViewModel> buildConfirmation
    ) {
        if (result.IsSuccess) {
            TempData["Success"] = successMessage;
            return RedirectToAction(redirectAction, "Home", routeValues);
        }

        IActionResult? accessResult = HandleMutationFailure(result.Error, "No fue posible completar la operación.");
        if (accessResult is not null) {
            return accessResult;
        }

        Result<string> replacement = await issueReplacement();
        if (replacement.IsFailure) {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        ClientOperationConfirmationViewModel confirmation = buildConfirmation();
        confirmation.ConfirmationToken = replacement.Value;
        return View("ConfirmOperation", confirmation);
    }

    private ClientOperationPageViewModel<TForm> OperationPage<TForm>(
        TForm form,
        string pageTitle,
        string navigationKey
    ) => new() {
        Form = form,
        PageTitle = pageTitle,
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Cliente),
        IsAuthenticated = true,
        ActiveNavigationItem = navigationKey,
    };

    private MyAccountTransactionsViewModel DecorateTransactions(
        MyAccountTransactionsViewModel source,
        IReadOnlyList<SelectOptionViewModel> accountOptions,
        IReadOnlyList<ClientAccountTransactionItemViewModel> transactions
    ) => new() {
        AccountNumber = source.AccountNumber,
        DateFrom = source.DateFrom,
        DateTo = source.DateTo,
        TransactionType = source.TransactionType,
        AccountOptions = accountOptions,
        TransactionTypeOptions = TransactionTypeOptions(source.TransactionType),
        Transactions = transactions,
        Pagination = source.Pagination,
        PageTitle = "Transacciones",
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Cliente),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.ClientTransactions,
    };

    private MyLoanDetailViewModel DecorateLoan(MyLoanDetailViewModel source) => new() {
        LoanId = source.LoanId,
        LoanNumber = source.LoanNumber,
        ApprovedPrincipal = source.ApprovedPrincipal,
        OutstandingAmount = source.OutstandingAmount,
        AnnualRate = source.AnnualRate,
        TermMonths = source.TermMonths,
        TotalInstallments = source.TotalInstallments,
        PaidInstallments = source.PaidInstallments,
        IsDelinquent = source.IsDelinquent,
        Amortization = source.Amortization,
        PageTitle = "Detalle del préstamo",
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Cliente),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.ClientHome,
    };

    private MyCardDetailViewModel DecorateCard(MyCardDetailViewModel source) => new() {
        CardId = source.CardId,
        LastFour = source.LastFour,
        CreditLimit = source.CreditLimit,
        AvailableCredit = source.AvailableCredit,
        CurrentDebt = source.CurrentDebt,
        Expiration = source.Expiration,
        Consumptions = source.Consumptions,
        Pagination = source.Pagination,
        PageTitle = "Detalle de tarjeta",
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Cliente),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.ClientHome,
    };

    private RemoveBeneficiaryViewModel BuildRemoveModel(
        BeneficiaryItemViewModel beneficiary,
        string token
    ) => new() {
        BeneficiaryId = beneficiary.BeneficiaryId,
        BeneficiaryName = $"{beneficiary.FirstName} {beneficiary.LastName}".Trim(),
        AccountNumber = beneficiary.AccountNumber,
        ConfirmationToken = token,
        PageTitle = "Eliminar beneficiario",
        Title = "Eliminar beneficiario",
        Message = "¿Está seguro que desea eliminar este beneficiario?",
        ConfirmButtonText = "Aceptar",
        CancelButtonText = "Cancelar",
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Cliente),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.ClientBeneficiaries,
    };

    private ClientOperationConfirmationViewModel BuildConfirmation(
        string token,
        string title,
        string message,
        string confirmAction,
        string? targetName = null,
        string? sourceAccountNumber = null,
        string? destinationAccountNumber = null,
        int? beneficiaryId = null,
        decimal? amount = null,
        string? accountNumber = null,
        int? cardId = null,
        int? loanId = null,
        decimal? interestAmount = null,
        decimal? totalToCharge = null
    ) => new() {
        ConfirmationToken = token,
        Title = title,
        OperationLabel = title,
        Message = message,
        ConfirmAction = confirmAction,
        TargetName = targetName,
        SourceAccountNumber = sourceAccountNumber,
        DestinationAccountNumber = destinationAccountNumber,
        BeneficiaryId = beneficiaryId,
        Amount = amount,
        AccountNumber = accountNumber,
        CardId = cardId,
        LoanId = loanId,
        InterestAmount = interestAmount,
        TotalToCharge = totalToCharge,
        PageTitle = title,
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Cliente),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.ClientHome,
    };

    private async Task<string?> PrepareFormTokenAsync(
        string currentToken,
        bool issueNewToken,
        bool formAvailable,
        string operation,
        CancellationToken cancellationToken
    ) {
        if (!formAvailable) {
            return string.Empty;
        }

        return issueNewToken || string.IsNullOrWhiteSpace(currentToken)
            ? await IssueFormTokenAsync(operation, cancellationToken)
            : currentToken;
    }

    private async Task<string?> IssueFormTokenAsync(
        string operation,
        CancellationToken cancellationToken
    ) {
        if (string.IsNullOrWhiteSpace(CurrentUserId)) {
            return null;
        }

        try {
            return await _tokens.IssueAsync(
                CurrentUserId,
                operation,
                "form",
                FormTokenLifetime,
                cancellationToken
            );
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception exception) {
            _logger.LogError(
                exception,
                "No se pudo emitir el nonce MVC del cliente para {Operation}",
                operation
            );
            return null;
        }
    }

    private async Task<bool> ValidateFormTokenAsync(
        string token,
        string operation,
        CancellationToken cancellationToken
    ) {
        if (string.IsNullOrWhiteSpace(token)
            || token.Length > 256
            || string.IsNullOrWhiteSpace(CurrentUserId)) {
            return false;
        }

        ConfirmationValidationResult result = await _tokens.ValidateAndConsumeAsync(
            token,
            CurrentUserId,
            operation,
            "form",
            cancellationToken
        );
        return result.IsValid;
    }

    private IActionResult HandleReadFailure(DomainError? error) {
        if (error?.Category == ErrorCategory.NotFound) {
            return NotFound();
        }

        if (error?.Category == ErrorCategory.Forbidden) {
            return RedirectToAccessDenied();
        }

        _logger.LogError("No se pudo cargar un recurso de Cliente: {ErrorCode}", error?.Code);
        return StatusCode(StatusCodes.Status500InternalServerError);
    }

    private IActionResult? HandleMutationFailure(DomainError? error, string fallback) {
        if (error?.Category == ErrorCategory.Forbidden) {
            return RedirectToAccessDenied();
        }

        if (error?.Category == ErrorCategory.Unauthorized) {
            return Challenge();
        }

        AddResultError(error, fallback);
        return null;
    }

    private RedirectToActionResult RedirectToAccessDenied() {
        TempData["AccessDeniedMessage"] = "No posee permisos para acceder a este recurso.";
        return RedirectToAction(nameof(AuthController.AccessDenied), "Auth");
    }

    private void AddResultError(DomainError? error, string fallback) =>
        ModelState.AddModelError(string.Empty, PublicOperationMessage(error) ?? fallback);

    private void AddFormTokenError() =>
        ModelState.AddModelError(
            string.Empty,
            "El formulario ya fue utilizado o expiró. Cárguelo nuevamente."
        );

    private string? CurrentUserId => User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);

    private string? CurrentUserName => User.Identity?.Name;

    private static SelectOptionViewModel[] AccountOptions(MyProductsViewModel products) =>
        products.Accounts
            .Select(account => new SelectOptionViewModel {
                Value = account.AccountNumber,
                Text = $"{account.AccountNumber} · {account.Type} · RD$ {Money(account.Balance)}",
            })
            .ToArray();

    private static SelectOptionViewModel[] CardOptions(MyProductsViewModel products) =>
        products.Cards
            .Select(card => new SelectOptionViewModel {
                Value = card.CardId.ToString(CultureInfo.InvariantCulture),
                Text = $"Tarjeta terminada en {card.LastFour} · Disponible RD$ {Money(card.AvailableCredit)}",
            })
            .ToArray();

    private static SelectOptionViewModel[] LoanOptions(MyProductsViewModel products) =>
        products.Loans
            .Select(loan => new SelectOptionViewModel {
                Value = loan.LoanId.ToString(CultureInfo.InvariantCulture),
                Text = $"Préstamo {loan.LoanNumber} · Pendiente RD$ {Money(loan.OutstandingAmount)}",
            })
            .ToArray();

    private static SelectOptionViewModel[] BeneficiaryOptions(
        IReadOnlyList<BeneficiaryItemViewModel> beneficiaries
    ) => beneficiaries
        .Select(beneficiary => new SelectOptionViewModel {
            Value = beneficiary.BeneficiaryId.ToString(CultureInfo.InvariantCulture),
            Text = $"{beneficiary.FirstName} {beneficiary.LastName} · {beneficiary.AccountNumber}".Trim(),
        })
        .ToArray();

    private static IReadOnlyList<SelectOptionViewModel> TransactionTypeOptions(string? selected) =>
        [
            new() { Value = string.Empty, Text = "Todos", IsSelected = string.IsNullOrWhiteSpace(selected) },
            new() { Value = "CRÉDITO", Text = "Créditos", IsSelected = selected == "CRÉDITO" },
            new() { Value = "DÉBITO", Text = "Débitos", IsSelected = selected == "DÉBITO" },
        ];

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    private static string? PublicOperationMessage(DomainError? error) => error?.Code switch {
        "Account.SourceNotFound" or "Account.DestinationNotFound" or "Account.NotActive"
            => "El número de cuenta ingresado no corresponde a una cuenta válida.",
        "Account.InsufficientFunds"
            => "El monto ingresado excede el saldo disponible de la cuenta.",
        "Account.MinimumActiveAccounts"
            => "Debe tener al menos dos cuentas de ahorro activas para realizar una transferencia entre cuentas.",
        "Card.NotFound" or "Card.NotActive"
            => "El número de tarjeta ingresado no corresponde a una tarjeta válida.",
        "Card.Expired" => "La tarjeta seleccionada se encuentra vencida.",
        "Card.NoDebt" => "La tarjeta seleccionada no tiene deuda pendiente.",
        "Card.InsufficientCredit"
            => "El avance solicitado excede el crédito disponible de la tarjeta seleccionada.",
        "Loan.NotFound" or "Loan.NotActive"
            => "El número de préstamo ingresado no corresponde a un préstamo válido.",
        "Loan.NoPendingInstallments"
            => "El préstamo seleccionado no tiene cuotas pendientes de pago.",
        "Operation.SameAccount"
            => "La cuenta de origen y la cuenta de destino no pueden ser la misma.",
        "Beneficiary.NotFound" => "La cuenta del beneficiario no se encuentra disponible.",
        "Beneficiary.OwnAccount"
            => "No puede agregar una cuenta propia como beneficiario. Utilice la opción Transferencia para mover fondos entre sus cuentas.",
        "Beneficiary.AlreadyExists" => "Esta cuenta ya se encuentra registrada como beneficiario.",
        _ => null,
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class ClientAccessExceptionFilterAttribute : ExceptionFilterAttribute {
    public override void OnException(ExceptionContext context) {
        if (context.Exception is not ForbiddenAccessException) {
            return;
        }

        context.Result = new RedirectToActionResult(
            nameof(AuthController.AccessDenied),
            "Auth",
            routeValues: null
        );
        context.ExceptionHandled = true;
    }
}
