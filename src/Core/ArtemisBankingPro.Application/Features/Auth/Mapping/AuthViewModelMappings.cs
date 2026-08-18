using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.ViewModels;

namespace ArtemisBankingPro.Application.Features.Auth.Mapping;

/// <summary>
/// Mappings that need server-owned context in addition to form input.
/// </summary>
public static class AuthViewModelMappings {
    /// <summary>
    /// Creates the reset request command without allowing the form to choose
    /// API/MVC roles or the callback URL.
    /// </summary>
    public static RequestPasswordResetCommand ToRequestPasswordResetCommand(
        this RequestPasswordResetViewModel source,
        IReadOnlyCollection<string> allowedRoles,
        string? callbackUrl = null
    ) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(allowedRoles);

        return new RequestPasswordResetCommand(source.UserName, allowedRoles, callbackUrl);
    }
}
