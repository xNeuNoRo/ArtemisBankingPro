using ArtemisBankingPro.Application.Features.CreditCard.ViewModels;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Features.CreditCard.Services;

public interface ICreditCardManagementService {
    Task<Result<CreditCardListViewModel>> GetCardsAsync(
        CreditCardListViewModel model,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<Result<CreditCardDetailViewModel>> GetCardAsync(
        int cardId,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<Result<AssignCreditCardResponse>> AssignAsync(
        string customerUserId,
        AssignCreditCardViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );

    Task<Result<CreditCardMutationResponse>> UpdateLimitAsync(
        int cardId,
        UpdateCardLimitViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );

    Task<Result> CancelAsync(
        int cardId,
        CancelCreditCardViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    );
}
