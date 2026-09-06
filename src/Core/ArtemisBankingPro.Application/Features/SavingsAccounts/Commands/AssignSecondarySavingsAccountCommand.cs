using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.Commands;

public sealed record AssignSecondarySavingsAccountCommand(
    string CustomerUserId,
    decimal InitialAmount
) : IRequest<Result<SavingsAccountResponse>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Administrador"];

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestFingerprint => $"{CustomerUserId}|{InitialAmount}";
}
