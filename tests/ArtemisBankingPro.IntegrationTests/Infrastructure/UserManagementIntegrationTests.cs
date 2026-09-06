using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Features.Users.Handlers;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class UserManagementIntegrationTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    private static async Task EnsureRoleAsync(IServiceProvider provider, string role) {
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }
    }

    private async Task<AppUser> CreateSeededUserAsync(string userName, string role, bool active = true) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        await EnsureRoleAsync(scope.ServiceProvider, role);

        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "Nombre",
            LastName = "Apellido",
            IdentityDocument = $"7000{userName.Length}0001",
            Active = active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        (await userManager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();

        return user;
    }

    private static CreateUserCommandHandler CreateUserHandler(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IUserAccountService>(),
            provider.GetRequiredService<IUserRepository>(),
            provider.GetRequiredService<ISavingsAccountRepository>(),
            provider.GetRequiredService<IFinancialOperationRepository>(),
            provider.GetRequiredService<IAccountTokenService>(),
            provider.GetRequiredService<IEmailService>(),
            provider.GetRequiredService<INumberGenerator>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<IBusinessClock>(),
            provider.GetRequiredService<ICurrentUserService>(),
            NullLogger<CreateUserCommandHandler>.Instance
        );

    private sealed class FixedCurrentUser : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId => "mgadmin-id";
        public string? UserName => "mgadmin";
        public string? Role => "Administrador";
        public int? CommerceId => null;
    }

    private ServiceProvider BuildProviderWithFixedUser(EmailCaptureService emailService) =>
        Fixture.BuildProvider(configure: services => {
            services.AddScoped<IEmailService>(_ => emailService);
            services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser());
        });

    [Fact]
    public async Task CreateClientUser_CreatesInactiveUserWithPrincipalAccount() {
        await CreateSeededUserAsync("mgadmin", "Administrador");

        var emailService = new EmailCaptureService();
        await using var provider = BuildProviderWithFixedUser(emailService);

        await using (var roleScope = provider.CreateAsyncScope()) {
            await EnsureRoleAsync(roleScope.ServiceProvider, "Cliente");
            await EnsureRoleAsync(roleScope.ServiceProvider, "Administrador");
        }

        CreateUserCommandHandler handler;
        await using (var scope = provider.CreateAsyncScope()) {
            handler = CreateUserHandler(scope.ServiceProvider);

            var result = await handler.Handle(
                new CreateUserCommand(
                    "María",
                    "Gómez",
                    "00187654321",
                    "maria.gomez@artemis.com",
                    "maria.gomez",
                    "P@ssw0rd123!",
                    "P@ssw0rd123!",
                    "Cliente",
                    InitialAmount: 5000m
                ),
                CancellationToken.None
            );

            result.IsSuccess.Should().BeTrue();
            result.Value.IsActive.Should().BeFalse();

            // El correo de activación fue generado (token directo, flujo API).
            Assert.IsType<AccountActivationTokenModel>(emailService.LastModel);
        }

        // El usuario existe inactivo y su cuenta principal tiene el balance inicial.
        await using (var scope = Fixture.Services.CreateAsyncScope()) {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            AppUser? user = await userManager.FindByNameAsync("maria.gomez");
            Assert.NotNull(user);
            user.Active.Should().BeFalse();
        }

        await using (var scope = Fixture.Services.CreateAsyncScope()) {
            var accountRepository = scope.ServiceProvider
                .GetRequiredService<ISavingsAccountRepository>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            string userId = (await userManager.FindByNameAsync("maria.gomez"))!.Id;
            var account = (await accountRepository.GetByOwnerAsync(userId)).Single();
            Assert.NotNull(account);
            account.Balance.Amount.Should().Be(5000m);
            account.Type.Should().Be(Domain.Accounts.Enums.AccountType.Primary);
            account.Status.Should().Be(Domain.Accounts.Enums.AccountStatus.Active);
        }
    }

    [Fact]
    public async Task CreateClientUser_DuplicateUserName_ReturnsConflict() {
        await CreateSeededUserAsync("dupadmin", "Administrador");

        var emailService = new EmailCaptureService();
        await using var provider = Fixture.BuildProvider(configure: services =>
            services.AddScoped<IEmailService>(_ => emailService)
        );

        await using var scope = provider.CreateAsyncScope();
        var handler = CreateUserHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new CreateUserCommand(
                "María",
                "Gómez",
                "00187654321",
                "maria.gomez@artemis.com",
                "dupadmin",
                "P@ssw0rd123!",
                "P@ssw0rd123!",
                "Cliente",
                InitialAmount: 100m
            ),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("User.UserNameExists");
    }

    [Fact]
    public async Task CreateClientUser_WhenAccountPersistenceThrows_RollsBackIdentityAndAccount() {
        await CreateSeededUserAsync("atomic-admin", "Administrador");

        var emailService = new EmailCaptureService();
        await using var provider = Fixture.BuildProvider(configure: services => {
            services.AddScoped<IEmailService>(_ => emailService);
            services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser());
            services.AddScoped<INumberGenerator>(_ => new ThrowingNumberGenerator());
        });

        await using (var roleScope = provider.CreateAsyncScope()) {
            await EnsureRoleAsync(roleScope.ServiceProvider, "Cliente");
            await EnsureRoleAsync(roleScope.ServiceProvider, "Administrador");
        }

        int accountCountBefore;
        await using (var snapshotScope = provider.CreateAsyncScope()) {
            accountCountBefore = await snapshotScope.ServiceProvider
                .GetRequiredService<BankingDbContext>()
                .SavingsAccounts
                .CountAsync();
        }

        string userName = $"rollback-{Guid.NewGuid():N}"[..30];
        await using (var scope = provider.CreateAsyncScope()) {
            var handler = CreateUserHandler(scope.ServiceProvider);
            Func<Task> act = () => handler.Handle(
                new CreateUserCommand(
                    "Rollback",
                    "User",
                    DigitsFromGuid(),
                    $"{userName}@example.test",
                    userName,
                    "P@ssw0rd123!",
                    "P@ssw0rd123!",
                    "Cliente",
                    InitialAmount: 100m
                ),
                CancellationToken.None
            ).AsTask();

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        await using var verificationScope = provider.CreateAsyncScope();
        var userManager = verificationScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.Null(await userManager.FindByNameAsync(userName));
        (await verificationScope.ServiceProvider.GetRequiredService<BankingDbContext>()
            .SavingsAccounts.CountAsync()).Should().Be(accountCountBefore);
    }

    [Fact]
    public async Task ChangeStatus_DeactivatesUser_PreventsLogin() {
        AppUser user = await CreateSeededUserAsync("statustest", "Cliente");

        await using var scope = Fixture.Services.CreateAsyncScope();
        var handler = new ChangeUserStatusCommandHandler(
            scope.ServiceProvider.GetRequiredService<IUserAccountService>()
        );

        var result = await handler.Handle(
            new ChangeUserStatusCommand(user.Id, IsActive: false),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();

        // El usuario inactivo no puede iniciar sesión.
        var userService = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var login = await userService.ValidateCredentialsAsync(
            "statustest",
            "P@ssw0rd123!",
            Domain.Enums.RoleSets.Mvc
        );
        login.Status.Should().Be(LoginStatus.Inactive);
    }

    private sealed class ThrowingNumberGenerator : INumberGenerator {
        public Task<string> NextAccountNumberAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("number generator unavailable");

        public Task<string> NextLoanNumberAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("number generator unavailable");

        public Task<string> NextCardNumberAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("number generator unavailable");
    }

    private static string DigitsFromGuid() =>
        new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).Concat("00000000000").Take(11).ToArray());
}
