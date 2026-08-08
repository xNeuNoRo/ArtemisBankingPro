using ArtemisBankingPro.Application.Models.Emails;
using RazorLight;

namespace ArtemisBankingPro.Infrastructure.Shared.Messaging;

/// <summary>
/// Renderizador de plantillas Razor con caché en memoria. Resuelve las
/// plantillas desde <c>Templates/Emails</c> del directorio base de la
/// aplicación.
/// </summary>
public sealed class RazorRenderer : IRazorRenderer {
    private const string TemplateRoot = "Templates/Emails";

    private readonly RazorLightEngine _engine;

    public RazorRenderer() {
        string templateDirectory = Path.Combine(AppContext.BaseDirectory, TemplateRoot);
        if (!Directory.Exists(templateDirectory)) {
            throw new InvalidOperationException(
                $"El directorio de plantillas de correo no existe en {templateDirectory}. "
                    + "Verifique que los templates .cshtml se copien al output."
            );
        }

        _engine = new RazorLightEngineBuilder()
            .UseFileSystemProject(templateDirectory)
            .UseMemoryCachingProvider()
            .DisableEncoding()
            .Build();
    }

    public async Task<string> RenderAsync<T>(T model, CancellationToken ct = default)
        where T : IEmailModel {
        ArgumentNullException.ThrowIfNull(model);

        string templateKey = $"{ResolveTemplateFileName(model.TemplateName)}.cshtml";
        return await _engine.CompileRenderAsync(templateKey, model);
    }

    /// <summary>
    /// Reduce el nombre del template a un nombre de archivo simple: rechaza
    /// separadores de ruta, nombres vacíos y traversal (<c>..</c>).
    /// </summary>
    private static string ResolveTemplateFileName(string templateName) {
        if (string.IsNullOrWhiteSpace(templateName)) {
            throw new ArgumentException(
                "El nombre del template de correo no puede estar vacío.",
                nameof(templateName)
            );
        }

        string fileName = Path.GetFileName(templateName);
        if (fileName != templateName || fileName.Contains("..", StringComparison.Ordinal)) {
            throw new ArgumentException(
                $"El nombre del template '{templateName}' no es un nombre de archivo válido.",
                nameof(templateName)
            );
        }

        return fileName;
    }
}
