using ArtemisBankingPro.Infrastructure.Persistence.Contexts;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

/// <summary>
/// Base para tests de SQL Server reales: contenedor compartido, base
/// reseteada entre tests y helpers de contexto.
/// </summary>
public abstract class SqlServerTestBase : IAsyncLifetime {
    protected SqlServerFixture Fixture { get; }

    protected SqlServerTestBase(SqlServerFixture fixture) {
        Fixture = fixture;
    }

    public Task InitializeAsync() => Fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    protected async Task<T> WithContextAsync<T>(Func<BankingDbContext, Task<T>> action) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        return await action(context);
    }

    protected Task WithContextAsync(Func<BankingDbContext, Task> action) =>
        WithContextAsync(async context => {
            await action(context);
            return true;
        });

    /// <summary>Proveedor aislado con registros o configuración adicionales.</summary>
    protected ServiceProvider BuildProvider(
        Action<IServiceCollection>? configure = null,
        IReadOnlyDictionary<string, string?>? extraConfiguration = null
    ) => Fixture.BuildProvider(configure, extraConfiguration);
}
