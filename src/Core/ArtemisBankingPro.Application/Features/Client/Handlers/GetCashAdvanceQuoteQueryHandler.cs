using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Cards.Policies;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

public sealed class GetCashAdvanceQuoteQueryHandler
    : IRequestHandler<GetCashAdvanceQuoteQuery, Result<CashAdvanceQuoteDto>> {
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly IBusinessClock _clock;

    public GetCashAdvanceQuoteQueryHandler(
        ICreditCardRepository creditCardRepository,
        ICurrentUserService currentUser,
        IBusinessClock clock
    ) {
        _creditCardRepository = creditCardRepository;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async ValueTask<Result<CashAdvanceQuoteDto>> Handle(
        GetCashAdvanceQuoteQuery message,
        CancellationToken cancellationToken
    ) {
        CreditCardEntity? card = await _creditCardRepository.GetByIdAsync(
            message.CardId,
            cancellationToken
        );
        if (card is null) {
            return Result.Failure<CashAdvanceQuoteDto>(
                DomainError.NotFound("Card.NotFound", "La tarjeta indicada no existe.")
            );
        }

        if (card.CustomerUserId != _currentUser.UserId) {
            throw new ForbiddenAccessException("La tarjeta no pertenece al cliente autenticado.");
        }

        Result<Money> amountResult = Money.Create(message.Amount);
        if (amountResult.IsFailure) {
            return Result.Failure<CashAdvanceQuoteDto>(amountResult.Error!);
        }

        Result<CashAdvanceQuote> quoteResult = CashAdvancePolicy.Calculate(
            amountResult.Value
        );
        if (quoteResult.IsFailure) {
            return Result.Failure<CashAdvanceQuoteDto>(quoteResult.Error!);
        }

        CashAdvanceQuote quote = quoteResult.Value;
        bool isEligible = card
            .CanAuthorizeCharge(quote.TotalCardCharge, _clock.Today)
            .IsSuccess;
        return Result.Success(
            new CashAdvanceQuoteDto(
                quote.Principal.Amount,
                quote.Interest.Amount,
                quote.TotalCardCharge.Amount,
                card.AvailableCredit.Amount - quote.TotalCardCharge.Amount,
                isEligible
            )
        );
    }
}
