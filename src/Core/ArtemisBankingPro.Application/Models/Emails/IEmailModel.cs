namespace ArtemisBankingPro.Application.Models.Emails;

/// <summary>
/// Contrato de los modelos de correo.
/// </summary>
public interface IEmailModel
{
    string Subject { get; }

    string TemplateName { get; }
}
