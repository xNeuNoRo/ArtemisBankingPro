using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Application.Features.Merchants.Handlers;
using ArtemisBankingPro.Application.Features.Merchants.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using MapsterMapper;
using Microsoft.AspNetCore.Identity;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

/// <summary>
/// Prueba el detalle de comercios sobre SQL Server e Identity reales
/// (spec §40, GET /api/commerce/{id}).
/// </summary>
[Collection("SqlServer")]
public sealed class MerchantDetailQueryTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 12, 12, 0, 0, TimeSpan.FromHours(-4));

    private static Merchant NewMerchant(
        string name,
        string email,
        string rnc,
        bool active = true
    ) {
        Merchant merchant = Merchant.Create(
            name,
            "Comercio de detalle",
            email,
            "8095550808",
            rnc,
            "admin",
            CreatedAt).Value;
        if (!active) {
            merchant.Deactivate(CreatedAt.AddHours(1)).IsSuccess.Should().BeTrue();
        }

        return merchant;
    }

    private async Task<AppUser> CreateComercioUserAsync(string userName) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roleManager.RoleExistsAsync(nameof(Roles.Comercio))) {
            await roleManager.CreateAsync(new IdentityRole(nameof(Roles.Comercio)));
        }

        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "Comercio",
            LastName = "Detalle",
            IdentityDocument = $"0000{userName.Length}0001",
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        (await userManager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, nameof(Roles.Comercio)))
            .Succeeded.Should().BeTrue();

        return user;
    }

    private async Task<Result<MerchantDetailDto>> QueryAsync(int merchantId) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        var handler = new GetMerchantByIdQueryHandler(
            new MerchantRepository(context),
            scope.ServiceProvider.GetRequiredService<IUserRepository>(),
            new ServiceMapper(null!, ArtemisBankingPro.Application.Common.Mapping.MapsterConfig.Create())
        );

        return await handler.Handle(new GetMerchantByIdQuery(merchantId), default);
    }

    [Fact]
    public async Task GetById_WithAssociatedUser_ReturnsDetailWithUser() {
        AppUser user = await CreateComercioUserAsync("comerciodetalle01");
        Merchant merchant = NewMerchant(
            "Comercio Con Usuario",
            "conusuario@example.com",
            "101000121");
        await WithContextAsync(async context => {
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();

            merchant.AssociateUser(user.Id, merchant.CreatedAt.AddDays(1))
                .IsSuccess.Should().BeTrue();
            await context.SaveChangesAsync();
        });

        Result<MerchantDetailDto> result = await QueryAsync(merchant.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(merchant.Id);
        result.Value.Name.Should().Be("Comercio Con Usuario");
        result.Value.Email.Should().Be("conusuario@example.com");
        result.Value.Rnc.Should().Be("101000121");
        result.Value.IsActive.Should().BeTrue();
        result.Value.CreatedAt.Should().Be(merchant.CreatedAt);
        Assert.NotNull(result.Value.AssociatedUser);
        result.Value.AssociatedUser.Id.Should().Be(user.Id);
        result.Value.AssociatedUser.UserName.Should().Be(user.UserName);
        result.Value.AssociatedUser.Email.Should().Be(user.Email);
        result.Value.AssociatedUser.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetById_WithoutAssociatedUser_ReturnsNullAssociatedUser() {
        Merchant merchant = NewMerchant("Comercio Solo", "solo@example.com", "101000122");
        await WithContextAsync(async context => {
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();
        });

        Result<MerchantDetailDto> result = await QueryAsync(merchant.Id);

        result.IsSuccess.Should().BeTrue();
        Assert.Null(result.Value.AssociatedUser);
    }

    [Fact]
    public async Task GetById_InactiveMerchant_ReportsInactiveState() {
        Merchant merchant = NewMerchant(
            "Comercio Inactivo",
            "inactivo@example.com",
            "101000123",
            active: false);
        await WithContextAsync(async context => {
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();
        });

        Result<MerchantDetailDto> result = await QueryAsync(merchant.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeFalse();
        result.Value.Name.Should().Be("Comercio Inactivo");
    }

    [Fact]
    public async Task GetById_UnknownMerchant_ReturnsCommerceNotFound() {
        Result<MerchantDetailDto> result = await QueryAsync(999999);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.NotFound");
        result.Error.Message.Should().Be("El comercio indicado no existe.");
    }
}
