using Microsoft.Extensions.Configuration;

namespace ArtemisBankingPro.Infrastructure.Shared.Configuration;

/// <summary>
/// Extensiones de configuración para obtener valores requeridos y secciones requeridas.
/// </summary>
public static class ConfigurationExtensions {
    public static string GetRequiredString(this IConfiguration configuration, string key) {
        string? value = configuration[key];
        if (string.IsNullOrWhiteSpace(value)) {
            throw new InvalidOperationException(
                $"Falta la clave de configuración requerida '{key}'."
            );
        }

        return value;
    }

    public static T GetRequiredSection<T>(this IConfiguration configuration, string sectionName)
        where T : new() {
        var section = configuration.GetSection(sectionName).Get<T>();
        if (section is null) {
            throw new InvalidOperationException(
                $"Falta la sección de configuración requerida '{sectionName}'."
            );
        }

        return section;
    }
}
