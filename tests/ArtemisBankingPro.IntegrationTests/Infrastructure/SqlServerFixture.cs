using ArtemisBankingPro.Infrastructure.Identity;
using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using ArtemisBankingPro.Infrastructure.Persistence;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using ArtemisBankingPro.Infrastructure.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Testcontainers.MsSql;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[CollectionDefinition("SqlServer")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;

/// <summary>
/// Contenedor SQL Server compartido por la colección: aplica las migraciones
/// de Persistence y de Identity una vez, y expone un proveedor de servicios
/// con AddPersistence + AddIdentityForWebApi (JWT).
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime {
    private MsSqlContainer? _container;

    public ServiceProvider Services { get; private set; } = null!;

    public string ConnectionString => _container!.GetConnectionString();

    public async Task InitializeAsync() {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();

        Services = BuildProvider();

        await using var scope = Services.CreateAsyncScope();
        var bankingContext = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        await bankingContext.Database.MigrateAsync();

        var identityContext = scope.ServiceProvider.GetRequiredService<IdentityContext>();
        await identityContext.Database.MigrateAsync();
    }

    /// <summary>
    /// Construye un proveedor aislado (mismo contenedor) con registros o
    /// configuración adicionales para tests específicos.
    /// </summary>
    public ServiceProvider BuildProvider(
        Action<IServiceCollection>? configure = null,
        IReadOnlyDictionary<string, string?>? extraConfiguration = null
    ) {
        var configurationData = new Dictionary<string, string?> {
            ["ConnectionStrings:ArtemisDb"] = ConnectionString,
            ["Security:Jwt:SecretKey"] = TestKeys.JwtSecretKey,
            ["Security:Jwt:Issuer"] = "artemis-tests",
            ["Security:Jwt:Audience"] = "artemis-tests-api",
            ["Security:AccountTokens:PepperKey"] = TestKeys.TokenPepperKey,
        };

        if (extraConfiguration is not null) {
            foreach ((string key, string? value) in extraConfiguration) {
                configurationData[key] = value;
            }
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddPersistence(configuration);
        services.AddIdentityForWebApi(configuration);
        services.AddSharedInfrastructure(configuration);
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    public async Task ResetAsync() {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();

        string[] dboTables =
        [
            "IdempotencyRecords",
            "AccountTransactions",
            "CardConsumptions",
            "FinancialOperations",
            "Beneficiaries",
            "Installments",
            "Loans",
            "CreditCards",
            "Merchants",
            "SavingsAccounts",
        ];

        string[] identityTables =
        [
            "AccountTokens",
            "UserClaims",
            "UserLogins",
            "UserTokens",
            "RoleClaims",
            "UserRoles",
            "Users",
            "Roles",
        ];

        foreach (string table in identityTables) {
#pragma warning disable EF1002 // Nombres de tabla de una lista constante, sin entrada de usuario.
            await context.Database.ExecuteSqlRawAsync($"DELETE FROM [Identity].[{table}]");
        }

        foreach (string table in dboTables) {
            await context.Database.ExecuteSqlRawAsync($"DELETE FROM dbo.[{table}]");
#pragma warning restore EF1002
        }
    }

    public async Task DisposeAsync() {
        await Services.DisposeAsync();
        if (_container is not null) {
            await _container.DisposeAsync();
        }
    }
}

/// <summary>Claves sintéticas de tests (nunca se usan fuera de pruebas).</summary>
public static class TestKeys {
    public const string JwtSecretKey =
        "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    public const string TokenPepperKey =
        "ZmUwMTIzNDU2Nzg5YWJjZGVmMDEyMzQ1Njc4OWFiY2Rl";
}
