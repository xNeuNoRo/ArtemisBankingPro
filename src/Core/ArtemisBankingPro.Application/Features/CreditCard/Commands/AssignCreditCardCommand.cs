using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.CreditCard.Commands;

/// <summary>
/// Asigna una nueva tarjeta de crédito a un cliente activo con cuenta
/// principal activa. El número de 16 dígitos se genera con el BIN propio y la
/// secuencia compartida (Luhn), el CVC se descarta tras persistir su digest
/// HMAC-SHA256, y la operación queda registrada como historial
/// <see cref="FinancialOperationKind.CardAssigned"/>.
/// </summary>
public sealed record AssignCreditCardCommand(
    string CustomerUserId,
    decimal CreditLimit,
    bool ConfirmHighRisk = false
) : IRequest<Result<AssignCreditCardResponse>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Administrador"];

    public string IdempotencyKey => $"assign-card-{CustomerUserId}-{CreditLimit}";

    public string RequestFingerprint => $"{CustomerUserId}|{CreditLimit}";
}
