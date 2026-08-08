using ArtemisBankingPro.Application.Models.Emails;

namespace ArtemisBankingPro.Application.Interfaces.Email;

/// <summary>
/// Envío de correo con plantillas Razor.
/// </summary>
public interface IEmailService
{
    Task SendAsync<T>(string recipient, T model, CancellationToken ct = default)
        where T : IEmailModel;
}

/// <summary>
/// Fallo informativo del envío de correo.
/// </summary>
public sealed class EmailSendException(string subject, Exception innerException)
    : Exception($"No fue posible enviar el correo con asunto '{subject}'.", innerException)
{
    public string Subject { get; } = subject;
}
