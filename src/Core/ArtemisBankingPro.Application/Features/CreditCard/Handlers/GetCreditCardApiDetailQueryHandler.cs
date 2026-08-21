using System.Globalization;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Application.Features.CreditCard.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.CreditCard.Handlers;

public sealed class GetCreditCardApiDetailQueryHandler
    : IRequestHandler<GetCreditCardApiDetailQuery, Result<CreditCardApiDetailDto>> {
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly IUserRepository _userRepository;

    public GetCreditCardApiDetailQueryHandler(
        ICreditCardRepository creditCardRepository,
        IUserRepository userRepository
    ) {
        _creditCardRepository = creditCardRepository;
        _userRepository = userRepository;
    }

    public async ValueTask<Result<CreditCardApiDetailDto>> Handle(
        GetCreditCardApiDetailQuery message,
        CancellationToken cancellationToken
    ) {
        var card = await _creditCardRepository.GetByIdAsync(message.CardId, cancellationToken);
        if (card is null) {
            return Result.Failure<CreditCardApiDetailDto>(
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
        if (customer is null) {
            return Result.Failure<CreditCardApiDetailDto>(
                DomainError.NotFound(
                    "Card.CustomerNotFound",
                    "El cliente asociado a la tarjeta no existe."
                )
            );
        }

        var consumptions = await _creditCardRepository.GetConsumptionsAsync(
            card.Id,
            cancellationToken
        );

        return Result.Success(
            new CreditCardApiDetailDto(
                card.Id.ToString(CultureInfo.InvariantCulture),
                $"************{card.LastFour}",
                card.LastFour,
                customer.Id,
                $"{customer.FirstName} {customer.LastName}".Trim(),
                card.CreditLimit.Amount,
                card.AvailableCredit.Amount,
                card.CurrentDebt.Amount,
                card.Expiration.ToString(),
                card.Status.ToString(),
                consumptions
                    .Select(consumption => new CreditCardApiConsumptionDto(
                        consumption.Id.ToString(CultureInfo.InvariantCulture),
                        consumption.Date,
                        consumption.Amount,
                        consumption.CommerceName,
                        consumption.Status.ToString()
                    ))
                    .ToArray()
            )
        );
    }
}
