using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using ArtemisBankingPro.Application.Features.CreditCard.Handlers;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.RegularExpressions;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed partial class AssignCreditCardTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string AdminId = "assign-admin-id";

    private sealed class FixedCurrentUser : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId => AdminId;
        public string? UserName => "assignadmin";
        public string? Role => "Administrador";
        public int? CommerceId => null;
    }

    private sealed class CapturingEmailService : IEmailService {
        public List<(string Recipient, object Model)> Sent { get; } = [];

        public Task SendAsync<T>(string recipient, T model, CancellationToken ct = default)
            where T : IEmailModel {
            Sent.Add((recipient, model));
            return Task.CompletedTask;
        }
    }

    private static async Task EnsureRoleAsync(IServiceProvider provider, string role) {
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }
    }

    private int _documentCounter;

    private async Task<AppUser> CreateUserAsync(
        string userName,
        string role,
        bool active = true
    ) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        await EnsureRoleAsync(scope.ServiceProvider, role);

        string document = $"60000{(_documentCounter++):D5}";
        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "Cliente",
            LastName = "Prueba",
            IdentityDocument = document,
            Active = active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        (await userManager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        return user;
    }

    private async Task CreatePrincipalAccountAsync(string userId) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var numberGenerator = scope.ServiceProvider.GetRequiredService<INumberGenerator>();
        var accountRepository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string rawNumber = await numberGenerator.NextAccountNumberAsync();
        var accountNumber = AccountNumber.Create(rawNumber).Value;
        var account = SavingsAccount.OpenPrimary(
            userId,
            accountNumber,
            Money.Zero,
            AdminId,
            DateTimeOffset.UtcNow
        ).Value;

        await unitOfWork.ExecuteInTransactionAsync(async ct => {
            await accountRepository.AddAsync(account, ct);
            return Result.Success();
        });
    }

    private ServiceProvider BuildProvider(CapturingEmailService emailService) =>
        Fixture.BuildProvider(
            configure: services => {
                services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser());
                services.AddScoped<IEmailService>(_ => emailService);
            },
            extraConfiguration: new Dictionary<string, string?> {
                ["Security:Card:FingerprintKey"] = TestKeys.JwtSecretKey,
                ["Security:Card:CvcPepperKey"] = TestKeys.TokenPepperKey,
            }
        );

    private static AssignCreditCardCommandHandler CreateHandler(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IUserRepository>(),
            provider.GetRequiredService<ISavingsAccountRepository>(),
            provider.GetRequiredService<ICreditCardRepository>(),
            provider.GetRequiredService<IFinancialOperationRepository>(),
            provider.GetRequiredService<INumberGenerator>(),
            provider.GetRequiredService<ICardSecurityService>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<IBusinessClock>(),
            provider.GetRequiredService<ICurrentUserService>(),
            provider.GetRequiredService<IEmailService>(),
            NullLogger<AssignCreditCardCommandHandler>.Instance
        );

    [Fact]
    public async Task AssignCard_ToActiveClientWithPrincipalAccount_CreatesCardAndRecordsOperation() {
        AppUser client = await CreateUserAsync("assignclient1", nameof(Roles.Cliente));
        await CreatePrincipalAccountAsync(client.Id);
        var emailService = new CapturingEmailService();

        await using var provider = BuildProvider(emailService);
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new AssignCreditCardCommand(client.Id, 15_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.ClientId.Should().Be(client.Id);
        result.Value.ClientFullName.Should().Be("Cliente Prueba");
        result.Value.CreditLimit.Should().Be(15_000m);
        result.Value.AvailableCredit.Should().Be(15_000m);
        result.Value.LastFour.Should().MatchRegex(@"^\d{4}$");
        result.Value.MaskedNumber.Should().Be($"************{result.Value.LastFour}");
        result.Value.Expiration.Should().MatchRegex(ExpirationPattern());
        result.Value.Status.Should().Be("Active");
        result.Value.CreatedAt.Should().NotBe(default);

        // La tarjeta persistió con la huella del número generado y límite.
        var cardRepository = scope.ServiceProvider.GetRequiredService<ICreditCardRepository>();
        var card = await cardRepository.GetByIdAsync(result.Value.CardId);
        Assert.NotNull(card);
        card.CustomerUserId.Should().Be(client.Id);
        card.LastFour.Should().Be(result.Value.LastFour);
        card.PanFingerprint.Should().MatchRegex(FingerprintPattern());
        card.CreditLimit.Amount.Should().Be(15_000m);
        card.CurrentDebt.Amount.Should().Be(0m);
        card.AvailableCredit.Amount.Should().Be(15_000m);
        card.AssignedByUserId.Should().Be(AdminId);

        // Operación de historial atómica sin referencia a la tarjeta nueva.
        await WithContextAsync(async context => {
            var operation = await context.FinancialOperations.SingleAsync(
                operation => operation.Kind == FinancialOperationKind.CardAssigned
            );
            operation.RequestedAmount.Should().Be(Money.Zero);
            operation.AppliedAmount.Should().Be(Money.Zero);
            operation.InterestAmount.Should().Be(Money.Zero);
            operation.CreditCardId.Should().BeNull();
            operation.InitiatedByUserId.Should().Be(AdminId);
            operation.AccountTransactions.Should().BeEmpty();
        });

        // Correo post-commit con los datos de la tarjeta.
        (string Recipient, object Model) sent = emailService.Sent.Should().ContainSingle().Subject;
        sent.Recipient.Should().Be(client.Email);
        var model = sent.Model.Should().BeOfType<CardAssignedModel>().Subject;
        model.CustomerName.Should().Be("Cliente Prueba");
        model.LastFour.Should().Be(result.Value.LastFour);
        model.CreditLimit.Amount.Should().Be(15_000m);
        model.Expiration.Should().Be(result.Value.Expiration);
    }

    [Fact]
    public async Task AssignCard_GeneratesUniqueFingerprintPerCard() {
        AppUser first = await CreateUserAsync("assignclient2", nameof(Roles.Cliente));
        AppUser second = await CreateUserAsync("assignclient3", nameof(Roles.Cliente));
        await CreatePrincipalAccountAsync(first.Id);
        await CreatePrincipalAccountAsync(second.Id);
        var emailService = new CapturingEmailService();

        await using var provider = BuildProvider(emailService);
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateHandler(scope.ServiceProvider);

        var firstResult = await handler.Handle(
            new AssignCreditCardCommand(first.Id, 10_000m),
            CancellationToken.None
        );
        var secondResult = await handler.Handle(
            new AssignCreditCardCommand(second.Id, 20_000m),
            CancellationToken.None
        );

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();

        await WithContextAsync(async context => {
            var cards = await context.CreditCards
                .Where(card => card.CustomerUserId == first.Id || card.CustomerUserId == second.Id)
                .ToListAsync();
            cards.Should().HaveCount(2);
            cards[0].PanFingerprint.Should().NotBe(cards[1].PanFingerprint);
            cards[0].LastFour.Should().NotBe(cards[1].LastFour);
        });

        emailService.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task AssignCard_ToClientWithoutPrincipalAccount_ReturnsNoPrincipalAccount() {
        AppUser client = await CreateUserAsync("assignclient4", nameof(Roles.Cliente));
        var emailService = new CapturingEmailService();

        await using var provider = BuildProvider(emailService);
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new AssignCreditCardCommand(client.Id, 15_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Card.NoPrincipalAccount");
        emailService.Sent.Should().BeEmpty();

        await WithContextAsync(async context => {
            (await context.CreditCards.AnyAsync(card => card.CustomerUserId == client.Id))
                .Should().BeFalse();
            (await context.FinancialOperations.AnyAsync()).Should().BeFalse();
        });
    }

    [Fact]
    public async Task AssignCard_ToInactiveClient_ReturnsCustomerNotActive() {
        AppUser client = await CreateUserAsync(
            "assignclient5",
            nameof(Roles.Cliente),
            active: false
        );
        await CreatePrincipalAccountAsync(client.Id);
        var emailService = new CapturingEmailService();

        await using var provider = BuildProvider(emailService);
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new AssignCreditCardCommand(client.Id, 15_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Card.CustomerNotActive");

        await WithContextAsync(async context => {
            (await context.CreditCards.AnyAsync(card => card.CustomerUserId == client.Id))
                .Should().BeFalse();
            (await context.FinancialOperations.AnyAsync()).Should().BeFalse();
        });
    }

    [Fact]
    public async Task AssignCard_ToUserWithoutClientRole_ReturnsCustomerNotClient() {
        AppUser cashier = await CreateUserAsync("assigncashier", nameof(Roles.Cajero));
        await CreatePrincipalAccountAsync(cashier.Id);
        var emailService = new CapturingEmailService();

        await using var provider = BuildProvider(emailService);
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new AssignCreditCardCommand(cashier.Id, 15_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Card.CustomerNotClient");

        await WithContextAsync(async context => {
            (await context.CreditCards.AnyAsync(card => card.CustomerUserId == cashier.Id))
                .Should().BeFalse();
            (await context.FinancialOperations.AnyAsync()).Should().BeFalse();
        });
    }

    [GeneratedRegex(@"^\d{2}/\d{2}$")]
    private static partial Regex ExpirationPattern();

    [GeneratedRegex(@"^[0-9a-f]{64}$")]
    private static partial Regex FingerprintPattern();
}
