using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;
using System.Globalization;

namespace ArtemisBankingPro.Application.Features.CreditCard.Commands;

/// <summary>
/// Modifica el límite de crédito de una tarjeta activa. El dominio valida que
/// el nuevo límite sea positivo y no menor que la deuda actual, y registra una
/// operación de historial <see cref="FinancialOperationKind.CardLimitChanged"/>
/// sin movimientos de saldo.
/// </summary>
public sealed record UpdateCardLimitCommand(int CardId, decimal NewLimit)
    : IRequest<Result<CreditCardMutationResponse>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Administrador"];

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestFingerprint => string.Join(
        '|',
        CardId.ToString(CultureInfo.InvariantCulture),
        NewLimit.ToString(CultureInfo.InvariantCulture)
    );
}
