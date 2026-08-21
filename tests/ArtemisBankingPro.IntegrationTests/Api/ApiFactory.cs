using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Testcontainers.MsSql;

namespace ArtemisBankingPro.IntegrationTests.Api;

[CollectionDefinition("Api", DisableParallelization = true)]
public sealed class ApiTestCollectionDefinition : ICollectionFixture<ApiFactory>;

/// <summary>
/// HTTP harness backed by the same SQL Server provider used by the application.
/// The container is started before the SUT host is materialized and migrations
/// are applied before the first test creates a client.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime {
    private readonly MsSqlContainer _container = new MsSqlBuilder(
        "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04"
    ).Build();

    public string ConnectionString => _container.GetConnectionString();

    public ApiEmailCapture EmailCapture { get; } = new();

    public async Task InitializeAsync() {
        await _container.StartAsync();

        _ = Services;

        using HttpClient client = CreateClient();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ArtemisDb", ConnectionString);
        builder.UseSetting("Security:Jwt:SecretKey", TestKeys.JwtSecretKey);
        builder.UseSetting("Security:Jwt:Issuer", "artemis-tests");
        builder.UseSetting("Security:Jwt:Audience", "artemis-tests-api");
        builder.UseSetting("Security:Jwt:ExpirationMinutes", "15");
        builder.UseSetting("Security:AccountTokens:PepperKey", TestKeys.TokenPepperKey);
        builder.UseSetting("Security:Card:FingerprintKey", TestKeys.TokenPepperKey);
        builder.UseSetting("Security:Card:CvcPepperKey", TestKeys.TokenPepperKey);
        builder.UseSetting("Api:UseHttpsRedirection", "false");
        builder.UseSetting("Api:ExposeDocumentation", "true");
        builder.UseSetting("Database:Initialization:SeedIdentityOnStartup", "false");
        builder.ConfigureServices(services =>
            services
                .AddMvcCore()
                .AddApplicationPart(typeof(TestExceptionController).Assembly)
        );
        builder.ConfigureTestServices(services =>
            services.AddSingleton<IEmailService>(EmailCapture)
        );
    }

    public new async Task DisposeAsync() {
        Dispose();
        await _container.DisposeAsync();
    }
}

public sealed record CapturedApiEmail(string Recipient, IEmailModel Model);

public sealed class ApiEmailCapture : IEmailService {
    private readonly List<CapturedApiEmail> _messages = [];

    public IReadOnlyList<CapturedApiEmail> Messages => _messages;

    public Task SendAsync<T>(string recipient, T model, CancellationToken ct = default)
        where T : IEmailModel {
        _messages.Add(new CapturedApiEmail(recipient, model));
        return Task.CompletedTask;
    }

    public void Clear() => _messages.Clear();
}
