using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Application.Features.CreditCard.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.CreditCard.Handlers;

/// <summary>
/// Detalle de una tarjeta de crédito con sus consumos paginados, los más
/// recientes primero.
/// </summary>
public sealed class GetCreditCardDetailQueryHandler
    : IRequestHandler<GetCreditCardDetailQuery, Result<CreditCardDetailDto>> {
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly IUserRepository _userRepository;

    public GetCreditCardDetailQueryHandler(
        ICreditCardRepository creditCardRepository,
        IUserRepository userRepository
    ) {
        _creditCardRepository = creditCardRepository;
        _userRepository = userRepository;
    }

    public async ValueTask<Result<CreditCardDetailDto>> Handle(
        GetCreditCardDetailQuery message,
        CancellationToken cancellationToken
    ) {
        var card = await _creditCardRepository.GetByIdAsync(
            message.CardId,
            cancellationToken
        );
        if (card is null) {
            return Result.Failure<CreditCardDetailDto>(
                DomainError.NotFound(
                    "Card.NotFound",
                    "La tarjeta indicada no existe."
                )
            );
        }

        var customer = await _userRepository.GetByIdAsync(
            card.CustomerUserId,
            cancellationToken
        );

        var page = new PageRequest(message.Page, message.PageSize);
        var consumptions = await _creditCardRepository.GetConsumptionsPagedAsync(
            card.Id,
            page,
            cancellationToken
        );

        var items = consumptions.Items
            .Select(consumption => new CardConsumptionDto(
                consumption.Id,
                consumption.Date,
                consumption.Amount,
                consumption.CommerceName,
                consumption.Status == FinancialOperationStatus.Approved
                    ? "APROBADO"
                    : "RECHAZADO"
            ))
            .ToList();

        return Result.Success(
            new CreditCardDetailDto(
                card.Id,
                $"************{card.LastFour}",
                card.LastFour,
                customer is null ? string.Empty : $"{customer.FirstName} {customer.LastName}".Trim(),
                card.CreditLimit.Amount,
                card.AvailableCredit.Amount,
                card.CurrentDebt.Amount,
                card.Expiration.ToString(),
                card.Status.ToString(),
                new PageResult<CardConsumptionDto>(
                    items,
                    consumptions.TotalCount,
                    page.Page,
                    page.PageSize
                )
            )
        );
    }
}
