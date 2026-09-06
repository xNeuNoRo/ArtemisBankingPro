using ArtemisBankingPro.Application.Common.Mapping;
using ArtemisBankingPro.Application.Features.Admin.DTOs;
using ArtemisBankingPro.Application.Features.Admin.Mapping;
using ArtemisBankingPro.Application.Features.Admin.ViewModels;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Application.Features.Cashier.ViewModels;
using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Application.Features.Client.Mapping;
using ArtemisBankingPro.Application.Features.Client.ViewModels;
using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Application.Features.CreditCard.Mapping;
using ArtemisBankingPro.Application.Features.CreditCard.ViewModels;
using ArtemisBankingPro.Application.Features.Loans.Commands;
using ArtemisBankingPro.Application.Features.Loans.DTOs;
using ArtemisBankingPro.Application.Features.Loans.Mapping;
using ArtemisBankingPro.Application.Features.Loans.ViewModels;
using ArtemisBankingPro.Application.Features.Merchants.Commands;
using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Application.Features.Merchants.Mapping;
using ArtemisBankingPro.Application.Features.Merchants.ViewModels;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Commands;
using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Mapping;
using ArtemisBankingPro.Application.Features.SavingsAccounts.ViewModels;
using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Features.Users.DTOs;
using ArtemisBankingPro.Application.Features.Users.Mapping;
using ArtemisBankingPro.Application.Features.Users.ViewModels;
using ArtemisBankingPro.Domain.Common.Pagination;
using MapsterMapper;

namespace ArtemisBankingPro.UnitTests.Application.Common.Mapping;

public sealed class ApplicationViewModelMappingCoverageTests {
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private static readonly ServiceMapper Mapper = new(null!, MapsterConfig.Create());

    [Fact]
    public void AdministrativeOutputs_MapIndicatorsEligibilityAndPagination() {
        AdminDashboardViewModel dashboard = Mapper.Map<AdminDashboardViewModel>(
            new AdminDashboardDto(10, 2, 6, 1, 4, 1, 9, 2, 3, 4, 125.75m)
        );
        EligibleClientsViewModel eligible = AdminMappingRegister.ToViewModel(
            new EligibleClientsResponse(
                new PageResult<EligibleClientDto>(
                    [new("client-1", "001", "Ana Perez", "ana@example.com", 250m)],
                    41,
                    2,
                    20
                ),
                250m
            ),
            Mapper
        );

        dashboard.ActiveClients.Should().Be(4);
        dashboard.AverageDebtPerClient.Should().Be(125.75m);
        eligible.Clients.Should().ContainSingle().Which.ClientId.Should().Be("client-1");
        eligible.AverageDebt.Should().Be(250m);
        eligible.Pagination.Page.Should().Be(2);
        eligible.Pagination.TotalItems.Should().Be(41);
        eligible.Pagination.TotalPages.Should().Be(3);
    }

    [Fact]
    public void UserAndMerchantOutputs_MapNestedServerDataWithoutCredentialFields() {
        UserDetailViewModel user = Mapper.Map<UserDetailViewModel>(
            new UserDetailResponse(
                "user-1",
                "ana",
                "001",
                "Ana",
                "Perez",
                "ana@example.com",
                "Cliente",
                true,
                OccurredAt,
                new UserMainAccountResponse("000000001", 500m, true, "ACTIVA")
            )
        );
        MerchantDetailViewModel merchant = Mapper.Map<MerchantDetailViewModel>(
            new MerchantDetailDto(
                7,
                "Comercio Uno",
                "Descripcion",
                "commerce@example.com",
                "8095550101",
                "101000099",
                true,
                OccurredAt,
                new MerchantUserDto("user-2", "commerce", "owner@example.com", true)
            )
        );

        Assert.NotNull(user.MainAccount);
        user.MainAccount.AccountNumber.Should().Be("000000001");
        user.Role.Should().Be("Cliente");
        Assert.NotNull(merchant.AssociatedUser);
        merchant.AssociatedUser.UserName.Should().Be("commerce");
        typeof(UserDetailViewModel).GetProperties().Select(property => property.Name)
            .Should().NotContain("Password");
        typeof(MerchantDetailViewModel).GetProperties().Select(property => property.Name)
            .Should().NotContain("Password");
    }

    [Fact]
    public void FinancialOutputs_PreserveServerStatusesAndPageMetadata() {
        LoanListViewModel loans = LoansMappingRegister.ToListViewModel(
            [new LoanListDto(3, "000000003", "client-1", "Ana Perez", 1_000m, 12, 2, 800m, 12m, 12, "ACTIVO", "AL DIA", OccurredAt)],
            2,
            20,
            21,
            Mapper,
            status: "ACTIVO",
            identification: "001"
        );
        CreditCardDetailViewModel card = CreditCardMappingRegister.ToDetailViewModel(
            new CreditCardDetailDto(
                4,
                "************1234",
                "1234",
                "Ana Perez",
                5_000m,
                4_000m,
                1_000m,
                "12/28",
                "ACTIVA",
                new PageResult<CardConsumptionDto>(
                    [new(8, OccurredAt, 250m, "Comercio Uno", "APROBADO")],
                    21,
                    2,
                    10
                )
            ),
            Mapper
        );
        AccountDetailViewModel account = SavingsAccountsMappingRegister.ToDetailViewModel(
            new AccountDetailDto(
                "000000001",
                "Ana Perez",
                750m,
                "PRINCIPAL",
                "ACTIVA",
                new PageResult<AccountTransactionDto>(
                    [new(9, OccurredAt, 100m, "CREDITO", "CAJERO", "Ana Perez", "APROBADA")],
                    31,
                    2,
                    10
                )
            ),
            Mapper
        );

        loans.Loans.Should().ContainSingle().Which.Status.Should().Be("ACTIVO");
        loans.Pagination.TotalPages.Should().Be(2);
        card.LastFour.Should().Be("1234");
        card.MaskedNumber.Should().Be("************1234");
        card.Consumptions.Should().ContainSingle().Which.Status.Should().Be("APROBADO");
        card.Pagination.Page.Should().Be(2);
        account.AccountNumber.Should().Be("000000001");
        account.Transactions.Should().ContainSingle().Which.Status.Should().Be("APROBADA");
        account.Pagination.TotalItems.Should().Be(31);
    }

    [Fact]
    public void ClientAndCashierOutputs_MapNestedResultsAndKeepCardDataMasked() {
        MyCardDetailViewModel card = ClientMappingRegister.ToCardDetailViewModel(
            new MyCardDetailDto(
                9,
                "4321",
                4_000m,
                3_500m,
                500m,
                "10/29",
                new PageResult<MyCardConsumptionDto>(
                    [new(12, OccurredAt, 125m, "Comercio Uno", "APROBADO")],
                    12,
                    1,
                    10
                )
            ),
            Mapper
        );
        CashierDashboardViewModel dashboard = Mapper.Map<CashierDashboardViewModel>(
            new CashierDashboardDto(8, 3, 2, 1)
        );
        ThirdPartyTransferResultViewModel transfer = Mapper.Map<ThirdPartyTransferResultViewModel>(
            new ProcessThirdPartyTransferResponse(
                Guid.NewGuid(),
                "000000001",
                "000000002",
                300m,
                OccurredAt,
                "APROBADA",
                "Notificacion pendiente"
            )
        );

        card.CardId.Should().Be(9);
        card.LastFour.Should().Be("4321");
        card.Consumptions.Should().ContainSingle().Which.CommerceName.Should().Be("Comercio Uno");
        dashboard.PaymentsToday.Should().Be(3);
        transfer.SourceAccountNumber.Should().Be("000000001");
        transfer.NotificationWarning.Should().Be("Notificacion pendiente");
        typeof(MyCardDetailViewModel).GetProperties().Select(property => property.Name)
            .Should().NotContain(property => property.Contains("Pan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InputMappings_UseServerContextForOwnershipAndIdempotency() {
        CreateCommerceUserCommand commerceUser = UsersMappingRegister.ToCreateCommerceCommand(
            new CreateCommerceUserViewModel {
                CommerceId = 999,
                FirstName = "Ana",
                LastName = "Perez",
                Identification = "001",
                Email = "ana@example.com",
                UserName = "ana",
                Password = "Password1!",
                ConfirmPassword = "Password1!",
            },
            Mapper,
            7,
            "users-operation",
            "https://artemis.local/activate"
        );
        CreateLoanCommand loan = LoansMappingRegister.ToCreateCommand(
            new CreateLoanViewModel {
                CapitalAmount = 1_000m,
                TermMonths = 12,
                AnnualInterestRate = 12m,
            },
            Mapper,
            "server-client",
            confirmHighRisk: true,
            "loan-operation"
        );
        AssignCreditCardCommand card = CreditCardMappingRegister.ToAssignCommand(
            new AssignCreditCardViewModel { CreditLimit = 5_000m },
            Mapper,
            "server-client",
            "card-operation"
        );
        AssignSecondarySavingsAccountCommand account =
            SavingsAccountsMappingRegister.ToAssignCommand(
                new AssignSecondaryAccountViewModel { InitialAmount = 100m },
                Mapper,
                "server-client",
                "account-operation"
            );
        CreateMerchantCommand merchant = MerchantsMappingRegister.ToCreateCommand(
            new CreateMerchantViewModel {
                Name = "Comercio Uno",
                Email = "commerce@example.com",
                PhoneNumber = "8095550101",
                Rnc = "101000099",
            },
            Mapper,
            "merchant-operation"
        );

        commerceUser.CommerceId.Should().Be(7);
        commerceUser.IdempotencyKey.Should().Be("users-operation");
        loan.CustomerUserId.Should().Be("server-client");
        loan.ConfirmHighRisk.Should().BeTrue();
        loan.IdempotencyKey.Should().Be("loan-operation");
        card.CustomerUserId.Should().Be("server-client");
        card.IdempotencyKey.Should().Be("card-operation");
        account.CustomerUserId.Should().Be("server-client");
        account.IdempotencyKey.Should().Be("account-operation");
        merchant.IdempotencyKey.Should().Be("merchant-operation");
    }
}
