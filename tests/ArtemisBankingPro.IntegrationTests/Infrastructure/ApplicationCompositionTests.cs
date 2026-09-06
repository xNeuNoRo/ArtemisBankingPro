using ArtemisBankingPro.Application;
using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Merchants.Commands;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Events;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Operations.Events;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

/// <summary>
/// Gate de composición (Fase 6): registra la capa Application completa
/// (Mediator + behaviors + validators), resuelve todos los handlers del
/// assembly con dependencias reales de Persistence/Shared, y ejecuta un
/// command real a través del pipeline con los tres behaviors activos.
/// </summary>
[Collection("SqlServer")]
public sealed class ApplicationCompositionTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string AdminId = "compose-admin";

    private sealed class FixedCurrentUser(string role) : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId => AdminId;
        public string? UserName => "composeadmin";
        public string? Role { get; } = role;
        public int? CommerceId => null;
    }

    private sealed class NoopEmailService : IEmailService {
        public Task SendAsync<T>(string recipient, T model, CancellationToken ct = default)
            where T : IEmailModel => Task.CompletedTask;
    }

    private ServiceProvider BuildApplicationProvider(
        string role = nameof(Roles.Administrador)
    ) => BuildProvider(
        configure: services => {
            services.AddApplication();
            services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser(role));
            services.AddScoped<IEmailService>(_ => new NoopEmailService());
        },
        extraConfiguration: new Dictionary<string, string?> {
            ["Security:Card:FingerprintKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
            ["Security:Card:CvcPepperKey"] = "ZmUwMTIzNDU2Nzg5YWJjZGVmMDEyMzQ1Njc4OWFiY2Rl",
        }
    );

    [Fact]
    public void AddApplication_ResolvesEveryRequestHandlerWithoutMissingDependencies() {
        using var provider = BuildApplicationProvider();
        using var scope = provider.CreateAsyncScope();
        var serviceProvider = scope.ServiceProvider;

        var handlerInterfaces = typeof(ServicesRegistration).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsClass: true })
            .SelectMany(type => type.GetInterfaces())
            .Where(iface => iface.IsGenericType
                && iface.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
            .Distinct()
            .ToList();

        handlerInterfaces.Should().NotBeEmpty();

        // GetRequiredService lanza si un handler quedó registrado con alguna
        // dependencia no resoluble por el contenedor real.
        foreach (Type handlerInterface in handlerInterfaces) {
            object handler = serviceProvider.GetRequiredService(handlerInterface);
            Assert.NotNull(handler);
        }

        // El consumidor de eventos de dominio (ADR-012) se resuelve en el
        // mismo contenedor que el DomainEventDispatcher de Persistence.
        var auditLogHandler = serviceProvider.GetRequiredService<
            IEventHandler<FinancialOperationApprovedEvent>
        >();
        Assert.NotNull(auditLogHandler);
    }

    [Fact]
    public void AddApplication_RegistersErrorMapperAsSingleton() {
        using var provider = BuildApplicationProvider();
        IErrorResponseMapper rootMapper = provider.GetRequiredService<IErrorResponseMapper>();

        using IServiceScope scope = provider.CreateScope();
        IErrorResponseMapper scopedMapper = scope.ServiceProvider
            .GetRequiredService<IErrorResponseMapper>();

        Assert.Same(rootMapper, scopedMapper);
    }

    [Fact]
    public async Task Mediator_ExecutesRealBehaviors_ValidationAuthorizationAndIdempotency() {
        using var provider = BuildApplicationProvider();
        using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var command = new CreateMerchantCommand(
            "Comercio Composición",
            null,
            "compose@example.com",
            "8095550777",
            "101000333"
        ) { IdempotencyKey = "composition-key-1" };

        // Behaviors reales: autorización (Administrador) + validación +
        // idempotencia + handler con repositorios reales.
        var first = await mediator.Send(command, CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        // Replay con la misma clave y actor → conflicto idempotente
        // determinista (el efecto no se re-aplica).
        Func<Task> replay = () => mediator.Send(command, CancellationToken.None).AsTask();
        await replay.Should().ThrowAsync<IdempotencyConflictException>();

        // Rol sin permiso → el AuthorizationBehavior deniega antes del
        // handler.
        using var clientProvider = BuildApplicationProvider(role: nameof(Roles.Cliente));
        using var clientScope = clientProvider.CreateAsyncScope();
        var clientMediator = clientScope.ServiceProvider.GetRequiredService<IMediator>();
        var forbidden = new CreateMerchantCommand(
            "Comercio No Permitido",
            null,
            "forbidden@example.com",
            "8095550888",
            "101000444"
        ) { IdempotencyKey = "composition-key-2" };
        Func<Task> forbiddenSend = () =>
            clientMediator.Send(forbidden, CancellationToken.None).AsTask();
        await forbiddenSend.Should().ThrowAsync<ForbiddenAccessException>();

        // El único comercio creado es el autorizado.
        await WithContextAsync(async context => {
            int stored = await context.Merchants.CountAsync(item =>
                item.Rnc == "101000333" || item.Rnc == "101000444");
            stored.Should().Be(1);
        });
    }
}
