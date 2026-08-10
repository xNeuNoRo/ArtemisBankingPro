using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Application.Features.CreditCard.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using Mediator;

namespace ArtemisBankingPro.Application.Features.CreditCard.Handlers;

/// <summary>
/// Lista tarjetas de crédito paginadas con filtro por estado y búsqueda por
/// cédula del cliente. Por defecto las activas aparecen primero.
/// </summary>
public sealed class GetCreditCardsPagedQueryHandler
    : IRequestHandler<GetCreditCardsPagedQuery, Result<PageResult<CreditCardSummaryDto>>> {
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly IUserRepository _userRepository;

    public GetCreditCardsPagedQueryHandler(
        ICreditCardRepository creditCardRepository,
        IUserRepository userRepository
    ) {
        _creditCardRepository = creditCardRepository;
        _userRepository = userRepository;
    }

    public async ValueTask<Result<PageResult<CreditCardSummaryDto>>> Handle(
        GetCreditCardsPagedQuery message,
        CancellationToken cancellationToken
    ) {
        // 1. Búsqueda por cédula: resolver el usuario cliente.
        string? customerUserId = null;
        if (!string.IsNullOrWhiteSpace(message.Identification)) {
            var customer = await _userRepository.GetByIdentityDocumentAsync(
                message.Identification,
                cancellationToken
            );
            if (customer is null) {
                return Result.Failure<PageResult<CreditCardSummaryDto>>(
                    DomainError.NotFound(
                        "Card.CustomerNotFound",
                        "No existe un cliente registrado con esta cédula."
                    )
                );
            }

            customerUserId = customer.Id;
        }

        // 2. Resolver el estado del filtro.
        CreditCardStatus? status = message.Status?.ToLowerInvariant() switch {
            "activa" => CreditCardStatus.Active,
            "cancelada" => CreditCardStatus.Cancelled,
            _ => null,
        };

        // 3. Consultar paginado.
        var page = new PageRequest(message.Page, message.PageSize);
        var paged = await _creditCardRepository.GetPagedAsync(
            customerUserId,
            status,
            page,
            cancellationToken
        );

        // 4. Resolver nombres de clientes en un solo viaje.
        var customerIds = paged.Items.Select(item => item.ClientId).Distinct().ToList();
        var customers = await _userRepository.GetByIdsAsync(customerIds, cancellationToken);
        var customerMap = customers.ToDictionary(c => c.Id);

        var items = paged.Items
            .Select(item => item with {
                ClientFullName = customerMap.TryGetValue(item.ClientId, out var customer)
                    ? $"{customer.FirstName} {customer.LastName}".Trim()
                    : string.Empty,
            })
            .ToList();

        return Result.Success(
            new PageResult<CreditCardSummaryDto>(
                items,
                paged.TotalCount,
                page.Page,
                page.PageSize
            )
        );
    }
}
