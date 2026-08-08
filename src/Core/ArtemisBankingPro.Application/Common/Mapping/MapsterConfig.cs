using System.Reflection;
using Mapster;

namespace ArtemisBankingPro.Application.Common.Mapping;

/// <summary>
/// Configuración global de Mapster, se encarga de registrar los profiles (IRegister) del
/// assembly de Application y define convenciones de mapeo por defecto.
/// </summary>
public static class MapsterConfig {
    /// <summary>
    /// Configura TypeAdapterConfig.GlobalSettings con los profiles del assembly
    /// de Application. Debe invocarse una vez al arrancar el host.
    /// </summary>
    public static void Configure() {
        TypeAdapterConfig.GlobalSettings.Scan(Assembly.GetExecutingAssembly());
    }
}
