using ArtemisBankingPro.Application.Common.Results;
using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Application.Features.CreditCard.Mapping;
using ArtemisBankingPro.Application.Features.CreditCard.Queries;
using ArtemisBankingPro.Application.Features.CreditCard.ViewModels;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Services;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using MapsterMapper;
using Mediator;

namespace ArtemisBankingPro.Application.Features.CreditCard.Services;

public sealed class CreditCardManagementService : ICreditCardManagementService {
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;
    private readonly ConfirmationGuard _confirmationGuard;
    private readonly IUserRepository _users;

    public CreditCardManagementService(
        IMediator mediator,
        IMapper mapper,
        ConfirmationGuard confirmationGuard,
        IUserRepository users
    ) {
        _mediator = mediator;
        _mapper = mapper;
        _confirmationGuard = confirmationGuard;
        _users = users;
    }

    public async Task<Result<CreditCardListViewModel>> GetCardsAsync(
        CreditCardListViewModel model,
        int page,
        int pageSize,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        Result<PageResult<CreditCardSummaryDto>> result = await _mediator.Send(
            new GetCreditCardsPagedQuery(page, pageSize, model.Status, model.Identification),
            ct
        );
        if (result.IsSuccess
            && !string.IsNullOrWhiteSpace(model.Identification)
            && result.Value.TotalCount == 0) {
            var customer = await _users.GetByIdentityDocumentAsync(model.Identification, ct);
            if (customer is null
                || !string.Equals(customer.Role, nameof(Roles.Cliente), StringComparison.Ordinal)) {
                return Result.Failure<CreditCardListViewModel>(
                    DomainError.NotFound(
                        "Card.CustomerNotFound",
                        "No existe un cliente registrado con esta cédula."
                    )
                );
            }
        }

        return result.MapValue(paged => CreditCardMappingRegister.ToListViewModel(
            paged.Items,
            paged.Page,
            paged.PageSize,
            paged.TotalCount,
            _mapper,
            model.Status,
            model.Identification
        ));
    }

    public async Task<Result<CreditCardDetailViewModel>> GetCardAsync(
        int cardId,
        int page,
        int pageSize,
        CancellationToken ct = default
    ) => (await _mediator.Send(new GetCreditCardDetailQuery(cardId, page, pageSize), ct))
        .MapValue(dto => CreditCardMappingRegister.ToDetailViewModel(dto, _mapper));

    public async Task<Result<AssignCreditCardResponse>> AssignAsync(
        string customerUserId,
        AssignCreditCardViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        AssignCreditCardCommand command = CreditCardMappingRegister.ToAssignCommand(
            model,
            _mapper,
            customerUserId,
            idempotencyKey
        );
        return await _mediator.Send(command, ct);
    }

    public async Task<Result<CreditCardMutationResponse>> UpdateLimitAsync(
        int cardId,
        UpdateCardLimitViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        UpdateCardLimitCommand command = CreditCardMappingRegister.ToUpdateLimitCommand(
            model,
            _mapper,
            cardId,
            idempotencyKey
        );
        return await _mediator.Send(command, ct);
    }

    public async Task<Result> CancelAsync(
        int cardId,
        CancelCreditCardViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        CancelCreditCardCommand command = CreditCardMappingRegister.ToCancelCommand(
            model,
            _mapper,
            cardId,
            idempotencyKey
        );
        Result confirmation = await _confirmationGuard.ValidateAsync(
            command,
            model.ConfirmationToken,
            ct
        );
        if (confirmation.IsFailure) {
            return confirmation;
        }

        return (await _mediator.Send(command with { IdempotencyKey = model.ConfirmationToken }, ct))
            .ToUnit();
    }
}
