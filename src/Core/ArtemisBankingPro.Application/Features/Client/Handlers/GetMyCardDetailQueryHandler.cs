using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

public sealed class GetMyCardDetailQueryHandler
    : IRequestHandler<GetMyCardDetailQuery, Result<MyCardDetailDto>> {
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly ICurrentUserService _currentUser;

    public GetMyCardDetailQueryHandler(
        ICreditCardRepository creditCardRepository,
        ICurrentUserService currentUser
    ) {
        _creditCardRepository = creditCardRepository;
        _currentUser = currentUser;
    }

    public async ValueTask<Result<MyCardDetailDto>> Handle(
        GetMyCardDetailQuery message,
        CancellationToken cancellationToken
    ) {
        CreditCardEntity? card = await _creditCardRepository.GetByIdAsync(
            message.CardId,
            cancellationToken
        );
        if (card is null) {
            return Result.Failure<MyCardDetailDto>(
                DomainError.NotFound("Card.NotFound", "La tarjeta indicada no existe.")
            );
        }

        if (card.CustomerUserId != _currentUser.UserId) {
            throw new ForbiddenAccessException("La tarjeta no pertenece al cliente autenticado.");
        }

        var page = new PageRequest(message.Page, message.PageSize);
        PageResult<CardConsumptionView> consumptions =
            await _creditCardRepository.GetConsumptionsPagedAsync(
                card.Id,
                page,
                cancellationToken
            );

        return Result.Success(
            new MyCardDetailDto(
                card.Id,
                card.LastFour,
                card.CreditLimit.Amount,
                card.AvailableCredit.Amount,
                card.CurrentDebt.Amount,
                card.Expiration.ToString(),
                new PageResult<MyCardConsumptionDto>(
                    consumptions.Items
                        .Select(item => new MyCardConsumptionDto(
                            item.Id,
                            item.Date,
                            item.Amount,
                            item.CommerceName,
                            item.Status == FinancialOperationStatus.Approved
                                ? "APROBADO"
                                : "RECHAZADO"
                        ))
                        .ToList(),
                    consumptions.TotalCount,
                    page.Page,
                    page.PageSize
                )
            )
        );
    }
}
