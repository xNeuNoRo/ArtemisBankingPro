using Microsoft.Extensions.Configuration;
using Serilog;

namespace ArtemisBankingPro.Infrastructure.Shared.Observability;

/// <summary>
/// Configuración compartida de Serilog: consola + archivo con rotación diaria,
/// template seguro (sin bodies de requests ni datos sensibles).
/// </summary>
public static class SerilogConfiguration
{
    private const string SafeOutputTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";

    public static LoggerConfiguration CreateArtemisLoggerConfiguration(
        IConfiguration configuration
    ) =>
        new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "ArtemisBankingPro")
            .WriteTo.Console(outputTemplate: SafeOutputTemplate)
            .WriteTo.File(
                path: "logs/artemis-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: SafeOutputTemplate
            )
            .ReadFrom.Configuration(configuration);
}
