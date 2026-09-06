using ArtemisBankingPro.Application.Common.Behaviors;
using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class IdempotencyBehaviorIntegrationTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string Key = "concurrent-key";
    private const string Fingerprint = "fingerprint";

    private sealed class FixedCurrentUser : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId => "actor-1";
        public string? UserName => "actor";
        public string? Role => "Administrador";
        public int? CommerceId => null;
    }

    private sealed record TestCommand(string IdempotencyKey, string RequestFingerprint)
        : IRequest<Result<Unit>>, IIdempotentCommand;

    private static IdempotencyBehavior<TestCommand, Result<Unit>> CreateBehavior(
        IServiceProvider services
    ) =>
        new(
            services.GetRequiredService<IIdempotencyRecordRepository>(),
            services.GetRequiredService<IUnitOfWork>(),
            services.GetRequiredService<ICurrentUserService>(),
            services.GetRequiredService<IBusinessClock>(),
            NullLogger<IdempotencyBehavior<TestCommand, Result<Unit>>>.Instance
        );

    private static MessageHandlerDelegate<TestCommand, Result<Unit>> CreateHandler(
        IServiceProvider services
    ) {
        IUnitOfWork unitOfWork = services.GetRequiredService<IUnitOfWork>();
        BankingDbContext context = services.GetRequiredService<BankingDbContext>();

        return async (_, ct) =>
            await unitOfWork.ExecuteInTransactionAsync<Unit>(
                async token => {
                    var merchant = Merchant.Create(
                        "Tienda Concurrente",
                        null,
                        "tienda@concurrente.com",
                        "8095550001",
                        "101000002",
                        "admin-1",
                        DateTimeOffset.UtcNow
                    ).Value;
                    await context.Merchants.AddAsync(merchant, token);
                    return Result.Success(Unit.Value);
                },
                ct: ct
            );
    }

    private static async Task<Result<Unit>> RunAsync(
        IdempotencyBehavior<TestCommand, Result<Unit>> behavior,
        TestCommand command,
        IServiceProvider services
    ) {
        try {
            return await behavior.Handle(command, CreateHandler(services), CancellationToken.None);
        }
        catch (IdempotencyConflictException) {
            return Result.Failure<Unit>(
                DomainError.Conflict("Test.IdempotencyConflict", "conflicto de idempotencia")
            );
        }
    }

    [Fact]
    public async Task ConcurrentRequestsWithSameKey_ApplyEffectExactlyOnce() {
        await using var provider = Fixture.BuildProvider(configure: services =>
            services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser()));

        await using var scopeA = provider.CreateAsyncScope();
        await using var scopeB = provider.CreateAsyncScope();

        var command = new TestCommand(Key, Fingerprint);

        Result<Unit>[] outcomes = await Task.WhenAll(
            RunAsync(CreateBehavior(scopeA.ServiceProvider), command, scopeA.ServiceProvider),
            RunAsync(CreateBehavior(scopeB.ServiceProvider), command, scopeB.ServiceProvider)
        );

        // La reserva atómica (unicidad key+actor) arbitra la carrera: exactamente
        // una solicitud gana y la otra recibe un conflicto determinista.
        outcomes.Count(result => result.IsSuccess).Should().Be(1);
        outcomes.Count(result => result.IsFailure).Should().Be(1);

        // El efecto (crear el comercio) ocurrió una única vez.
        await using var verificationScope = provider.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<BankingDbContext>();
        int merchantCount = await context.Merchants.CountAsync();
        merchantCount.Should().Be(1);

        IdempotencyRecord record = await context.Set<IdempotencyRecord>().SingleAsync();
        record.Status.Should().Be(IdempotencyStatus.Completed);
    }

    [Fact]
    public async Task HandlerException_LeavesRecordInProgressForUnknownOutcome() {
        await using var provider = Fixture.BuildProvider(configure: services =>
            services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser()));
        await using var scope = provider.CreateAsyncScope();
        var behavior = CreateBehavior(scope.ServiceProvider);

        Func<Task> act = () => behavior.Handle(
            new TestCommand("exception-key", Fingerprint),
            (_, _) => throw new InvalidOperationException("commit outcome unknown"),
            CancellationToken.None
        ).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();

        await using var verificationScope = provider.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<BankingDbContext>();
        var record = await context.Set<IdempotencyRecord>().SingleAsync();
        record.Status.Should().Be(IdempotencyStatus.InProgress);
    }

}
