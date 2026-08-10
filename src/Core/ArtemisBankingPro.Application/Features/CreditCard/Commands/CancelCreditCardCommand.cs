using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.CreditCard.Commands;

/// <summary>
/// Cancela una tarjeta de crédito activa sin deuda. La cancelación deja la
/// tarjeta en estado <c>Cancelada</c> y registra una operación de historial
/// <see cref="FinancialOperationKind.CardCancelled"/> sin movimientos de saldo.
/// </summary>
public sealed record CancelCreditCardCommand(int CardId)
    : IRequest<Result<Unit>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Administrador"];

    public string IdempotencyKey => $"cancel-card-{CardId}";

    public string RequestFingerprint => $"{CardId}";
}
