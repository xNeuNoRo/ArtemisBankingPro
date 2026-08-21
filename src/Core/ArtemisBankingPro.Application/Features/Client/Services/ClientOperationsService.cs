using ArtemisBankingPro.Application.Common.Results;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Common.Time;
using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Application.Features.Client.Mapping;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Features.Client.ViewModels;
using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Application.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using MapsterMapper;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Services;

public sealed class ClientOperationsService : IClientOperationsService {
    private const string ConfirmationPreviewKey = "mvc-confirmation-preview";
    private static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(30);

    private readonly IMediator _mediator;
    private readonly IMapper _mapper;
    private readonly ConfirmationGuard _confirmationGuard;
    private readonly IBusinessClock _clock;

    public ClientOperationsService(
        IMediator mediator,
        IMapper mapper,
        ConfirmationGuard confirmationGuard,
        IBusinessClock clock
    ) {
        _mediator = mediator;
        _mapper = mapper;
        _confirmationGuard = confirmationGuard;
        _clock = clock;
    }

    public async Task<Result<ClientDashboardViewModel>> GetDashboardAsync(
        CancellationToken ct = default
    ) => (await _mediator.Send(new GetMyProductsQuery(), ct))
        .MapValue(dto => ClientMappingRegister.ToDashboardViewModel(dto, _mapper));

    public async Task<Result<MyAccountTransactionsViewModel>> GetAccountTransactionsAsync(
        MyAccountTransactionsViewModel model,
        int page,
        int pageSize,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        (DateTimeOffset? dateFrom, DateTimeOffset? dateTo) = BusinessDateRange.NormalizeInclusive(
            _clock.BusinessTimeZone,
            model.DateFrom,
            model.DateTo
        );
        Result<PageResult<AccountTransactionDto>> result =
            await _mediator.Send(
                new GetMyAccountTransactionsQuery(
                    model.AccountNumber,
                    dateFrom,
                    dateTo,
                    model.TransactionType,
                    page,
                    pageSize
                ),
                ct
            );
        return result.MapValue(paged => ClientMappingRegister.ToAccountTransactionsViewModel(
            model.AccountNumber,
            paged.Items,
            paged.Page,
            paged.PageSize,
            paged.TotalCount,
            _mapper,
            model.DateFrom,
            model.DateTo,
            model.TransactionType
        ));
    }

    public async Task<Result<MyLoanDetailViewModel>> GetLoanAsync(
        int loanId,
        CancellationToken ct = default
    ) => (await _mediator.Send(new GetMyLoanDetailQuery(loanId), ct))
        .MapValue(dto => ClientMappingRegister.ToLoanDetailViewModel(dto, _mapper));

    public async Task<Result<MyCardDetailViewModel>> GetCardAsync(
        int cardId,
        int page,
        int pageSize,
        CancellationToken ct = default
    ) => (await _mediator.Send(new GetMyCardDetailQuery(cardId, page, pageSize), ct))
        .MapValue(dto => ClientMappingRegister.ToCardDetailViewModel(dto, _mapper));

    public async Task<Result<BeneficiaryListViewModel>> GetBeneficiariesAsync(
        CancellationToken ct = default
    ) => (await _mediator.Send(new GetMyBeneficiariesQuery(), ct))
        .MapValue(items => ClientMappingRegister.ToBeneficiaryListViewModel(items, _mapper));

    public async Task<Result<CashAdvanceQuoteViewModel>> GetCashAdvanceQuoteAsync(
        CashAdvanceQuoteViewModel model,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        return (await _mediator.Send(
            new GetCashAdvanceQuoteQuery(model.CardId, model.Amount ?? 0m),
            ct
        )).MapValue(dto => ClientMappingRegister.ToCashAdvanceQuoteViewModel(
            dto,
            model.CardId,
            model.Amount ?? 0m,
            _mapper
        ));
    }

    public async Task<Result<ClientTransferTargetViewModel>> GetExpressTransferTargetAsync(
        string destinationAccountNumber,
        CancellationToken ct = default
    ) => (await _mediator.Send(
        new GetClientTransferTargetQuery(destinationAccountNumber),
        ct
    )).MapValue(target => new ClientTransferTargetViewModel {
        AccountNumber = target.AccountNumber,
        FirstName = target.FirstName,
        LastName = target.LastName,
    });

    public Task<Result<string>> IssueAddBeneficiaryConfirmationAsync(
        AddBeneficiaryViewModel model,
        CancellationToken ct = default
    ) => IssueConfirmationAsync(
        ClientMappingRegister.ToAddBeneficiaryCommand(model, _mapper, ConfirmationPreviewKey),
        ct
    );

    public Task<Result<string>> IssueRemoveBeneficiaryConfirmationAsync(
        int beneficiaryId,
        CancellationToken ct = default
    ) => IssueConfirmationAsync(
        new RemoveBeneficiaryCommand(beneficiaryId) {
            IdempotencyKey = ConfirmationPreviewKey,
        },
        ct
    );

    public Task<Result<string>> IssueExpressTransferConfirmationAsync(
        ExpressTransactionViewModel model,
        CancellationToken ct = default
    ) => IssueConfirmationAsync(
        ClientMappingRegister.ToExpressTransactionCommand(model, _mapper, ConfirmationPreviewKey),
        ct
    );

    public Task<Result<string>> IssueBeneficiaryTransferConfirmationAsync(
        BeneficiaryTransferViewModel model,
        CancellationToken ct = default
    ) => IssueConfirmationAsync(
        ClientMappingRegister.ToBeneficiaryTransferCommand(model, _mapper, ConfirmationPreviewKey),
        ct
    );

    public Task<Result<string>> IssueOwnAccountsTransferConfirmationAsync(
        OwnAccountsTransferViewModel model,
        CancellationToken ct = default
    ) => IssueConfirmationAsync(
        ClientMappingRegister.ToOwnAccountsTransferCommand(model, _mapper, ConfirmationPreviewKey),
        ct
    );

    public Task<Result<string>> IssueCardPaymentConfirmationAsync(
        ClientCardPaymentViewModel model,
        CancellationToken ct = default
    ) => IssueConfirmationAsync(
        ClientMappingRegister.ToClientCardPaymentCommand(model, _mapper, ConfirmationPreviewKey),
        ct
    );

    public Task<Result<string>> IssueLoanPaymentConfirmationAsync(
        ClientLoanPaymentViewModel model,
        CancellationToken ct = default
    ) => IssueConfirmationAsync(
        ClientMappingRegister.ToClientLoanPaymentCommand(model, _mapper, ConfirmationPreviewKey),
        ct
    );

    public Task<Result<string>> IssueCashAdvanceConfirmationAsync(
        CashAdvanceViewModel model,
        CancellationToken ct = default
    ) => IssueConfirmationAsync(
        ClientMappingRegister.ToCashAdvanceCommand(model, _mapper, ConfirmationPreviewKey),
        ct
    );

    public async Task<Result> AddBeneficiaryAsync(
        AddBeneficiaryViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        return await ExecuteConfirmedAsync(
            ClientMappingRegister.ToAddBeneficiaryCommand(model, _mapper, idempotencyKey),
            idempotencyKey,
            ct
        );
    }

    public async Task<Result> RemoveBeneficiaryAsync(
        int beneficiaryId,
        RemoveBeneficiaryViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        RemoveBeneficiaryCommand command = ClientMappingRegister.ToRemoveBeneficiaryCommand(
            model,
            beneficiaryId,
            idempotencyKey
        );
        return await ExecuteConfirmedAsync(
            command with { IdempotencyKey = model.ConfirmationToken },
            model.ConfirmationToken,
            ct
        );
    }

    public async Task<Result> ExpressTransferAsync(
        ExpressTransactionViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        return await ExecuteConfirmedAsync(
            ClientMappingRegister.ToExpressTransactionCommand(model, _mapper, idempotencyKey),
            idempotencyKey,
            ct
        );
    }

    public async Task<Result> BeneficiaryTransferAsync(
        BeneficiaryTransferViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        return await ExecuteConfirmedAsync(
            ClientMappingRegister.ToBeneficiaryTransferCommand(model, _mapper, idempotencyKey),
            idempotencyKey,
            ct
        );
    }

    public async Task<Result> OwnAccountsTransferAsync(
        OwnAccountsTransferViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        return await ExecuteConfirmedAsync(
            ClientMappingRegister.ToOwnAccountsTransferCommand(model, _mapper, idempotencyKey),
            idempotencyKey,
            ct
        );
    }

    public async Task<Result> PayCardAsync(
        ClientCardPaymentViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        return await ExecuteConfirmedAsync(
            ClientMappingRegister.ToClientCardPaymentCommand(model, _mapper, idempotencyKey),
            idempotencyKey,
            ct
        );
    }

    public async Task<Result> PayLoanAsync(
        ClientLoanPaymentViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        return await ExecuteConfirmedAsync(
            ClientMappingRegister.ToClientLoanPaymentCommand(model, _mapper, idempotencyKey),
            idempotencyKey,
            ct
        );
    }

    public async Task<Result> CashAdvanceAsync(
        CashAdvanceViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        return await ExecuteConfirmedAsync(
            ClientMappingRegister.ToCashAdvanceCommand(model, _mapper, idempotencyKey),
            idempotencyKey,
            ct
        );
    }

    private async Task<Result<string>> IssueConfirmationAsync<TCommand>(
        TCommand command,
        CancellationToken ct
    ) where TCommand : IRequest<Result<Unit>>, IIdempotentCommand =>
        await _confirmationGuard.IssueAsync(command, ConfirmationLifetime, ct);

    private async Task<Result> ExecuteConfirmedAsync<TCommand>(
        TCommand command,
        string confirmationToken,
        CancellationToken ct
    ) where TCommand : IRequest<Result<Unit>>, IIdempotentCommand {
        Result confirmation = await _confirmationGuard.ValidateAsync(
            command,
            confirmationToken,
            ct
        );
        return confirmation.IsFailure
            ? confirmation
            : (await _mediator.Send(command, ct)).ToUnit();
    }
}
