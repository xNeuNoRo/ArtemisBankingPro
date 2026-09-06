using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Auth.Commands;

/// <summary>
/// Activa la cuenta de un usuario mediante un token de activación de un solo
/// uso enviado por correo. Endpoint público (flujo MVC y API).
/// </summary>
public sealed record ActivateAccountCommand(string Token)
    : IRequest<Result<Unit>>;
