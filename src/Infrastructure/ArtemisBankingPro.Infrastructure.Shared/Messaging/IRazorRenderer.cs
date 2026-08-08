using ArtemisBankingPro.Application.Models.Emails;

namespace ArtemisBankingPro.Infrastructure.Shared.Messaging;

/// <summary>
/// Renderiza plantillas Razor de correo a partir del nombre del template.
/// </summary>
public interface IRazorRenderer {
    Task<string> RenderAsync<T>(T model, CancellationToken ct = default)
        where T : IEmailModel;
}
