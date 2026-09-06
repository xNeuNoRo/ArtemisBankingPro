using System.ComponentModel.DataAnnotations;
using ArtemisBankingPro.Application.Common.Mapping;
using ArtemisBankingPro.Application.Features.Merchants.Commands;
using ArtemisBankingPro.Application.Features.Merchants.ViewModels;
using ArtemisBankingPro.Application.Features.CreditCard.Mapping;
using ArtemisBankingPro.Application.Features.CreditCard.ViewModels;
using ArtemisBankingPro.Application.Features.Loans.Mapping;
using ArtemisBankingPro.Application.Features.Loans.Queries;
using ArtemisBankingPro.Application.Features.Loans.ViewModels;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Mapping;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Queries;
using ArtemisBankingPro.Application.Features.SavingsAccounts.ViewModels;
using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Features.Users.Queries;
using ArtemisBankingPro.Application.Features.Users.Mapping;
using ArtemisBankingPro.Application.Features.Users.ViewModels;
using ArtemisBankingPro.Application.Features.CreditCard.Queries;
using MapsterMapper;

namespace ArtemisBankingPro.UnitTests.Application.Features.Administrative.ViewModels;

public sealed class AdministrativeViewModelTests {
    private static readonly ServiceMapper Mapper = new(null!, MapsterConfig.Create());

    [Fact]
    public void MapsterConfiguration_CompilesAllApplicationMappings() {
        Action compile = () => MapsterConfig.Create().Compile();

        compile.Should().NotThrow();
    }

    [Fact]
    public void CreateUserMapping_UsesServerOwnedCallbackAndIdempotencyContext() {
        CreateUserViewModel source = new() {
            FirstName = "Ana",
            LastName = "Pérez",
            Identification = "001",
            Email = "ana@example.com",
            UserName = "ana",
            Password = "Password1!",
            ConfirmPassword = "Password1!",
            Role = "Cliente",
            InitialAmount = 100m,
        };

        var command = UsersMappingRegister.ToCreateCommand(
            source,
            Mapper,
            "server-operation-1",
            "https://app.example.test/activate"
        );

        command.IdempotencyKey.Should().Be("server-operation-1");
        command.CallbackUrl.Should().Be("https://app.example.test/activate");
        command.InitialAmount.Should().Be(100m);
    }

    [Fact]
    public void AdministrativeMappings_UseServerRouteIds() {
        UpdateUserViewModel user = new() {
            FirstName = "Ana",
            LastName = "Pérez",
            Identification = "001",
            Email = "ana@example.com",
            UserName = "ana",
        };
        UpdateLoanRateViewModel loan = new() { AnnualInterestRate = 8m };
        UpdateCardLimitViewModel card = new() { NewLimit = 10000m };
        CancelSecondaryAccountViewModel account = new() { ConfirmationToken = "nonce" };

        UsersMappingRegister.ToUpdateCommand(user, Mapper, "server-user", "user-op")
            .UserId.Should().Be("server-user");
        LoansMappingRegister.ToUpdateRateCommand(loan, Mapper, 7, "loan-op")
            .LoanId.Should().Be(7);
        CreditCardMappingRegister.ToUpdateLimitCommand(card, Mapper, 8, "card-op")
            .CardId.Should().Be(8);
        SavingsAccountsMappingRegister.ToCancelCommand(
                account,
                Mapper,
                "000000001",
                "account-op"
            )
            .AccountNumber.Should().Be("000000001");
    }

    [Fact]
    public void DirectMappings_DoNotTrustPostedRouteIdentifiers() {
        ChangeUserStatusCommand userStatus = Mapper.Map<ChangeUserStatusCommand>(
            new ChangeUserStatusViewModel { UserId = "attacker-user", IsActive = true }
        );
        CreateCommerceUserCommand commerceUser = Mapper.Map<CreateCommerceUserCommand>(
            new CreateCommerceUserViewModel {
                CommerceId = 999,
                FirstName = "Ana",
                LastName = "Pérez",
                Identification = "001",
                Email = "ana@example.com",
                UserName = "ana",
                Password = "Password1!",
                ConfirmPassword = "Password1!",
            }
        );
        GetUserByIdQuery userDetail = Mapper.Map<GetUserByIdQuery>(
            new UserDetailViewModel { UserId = "attacker-user" }
        );
        GetLoanDetailQuery loanDetail = Mapper.Map<GetLoanDetailQuery>(
            new LoanDetailViewModel { LoanId = 999 }
        );
        GetCreditCardDetailQuery cardDetail = Mapper.Map<GetCreditCardDetailQuery>(
            new CreditCardDetailViewModel { Id = 999 }
        );
        GetAccountTransactionsQuery accountDetail = Mapper.Map<GetAccountTransactionsQuery>(
            new AccountDetailViewModel { AccountNumber = "999999999" }
        );
        ChangeMerchantStatusCommand merchantStatus = Mapper.Map<ChangeMerchantStatusCommand>(
            new ChangeMerchantStatusViewModel {
                MerchantId = 999,
                IsActive = true,
            }
        );

        userStatus.UserId.Should().BeEmpty();
        commerceUser.CommerceId.Should().Be(0);
        userDetail.UserId.Should().BeEmpty();
        loanDetail.LoanId.Should().Be(0);
        cardDetail.CardId.Should().Be(0);
        accountDetail.AccountNumber.Should().BeEmpty();
        merchantStatus.MerchantId.Should().Be(0);
    }

    [Fact]
    public void FormValidation_RejectsFinancialAndRoleBoundaryValues() {
        CreateUserViewModel user = new() {
            FirstName = "Ana",
            LastName = "Pérez",
            Identification = "001",
            Email = "ana@example.com",
            UserName = "ana",
            Password = "Password1!",
            ConfirmPassword = "Password1!",
            Role = "Cajero",
            InitialAmount = 1m,
        };
        CreateLoanViewModel loan = new() {
            CapitalAmount = 0m,
            TermMonths = 7,
            AnnualInterestRate = -1m,
        };

        ValidationResult[] userErrors = Validate(user);
        ValidationResult[] loanErrors = Validate(loan);

        userErrors.Select(error => error.ErrorMessage)
            .Should().Contain("El monto inicial solo aplica para usuarios con rol Cliente.");
        loanErrors.Select(error => error.ErrorMessage)
            .Should().Contain("El plazo seleccionado no es válido.");
        loanErrors.Select(error => error.ErrorMessage)
            .Should().Contain("El monto a prestar debe ser mayor que cero.");
        loanErrors.Select(error => error.ErrorMessage)
            .Should().Contain("La tasa de interés anual no puede ser negativa.");
    }

    [Fact]
    public void ReadViewModels_ContainNoSensitiveCardOrCredentialFields() {
        Type[] viewModels = [
            typeof(CreditCardSummaryViewModel),
            typeof(CreditCardDetailViewModel),
            typeof(CardConsumptionViewModel),
            typeof(UserDetailViewModel),
            typeof(UserListItemViewModel),
        ];
        string[] forbidden = ["Pan", "Cvc", "Password", "Jwt", "Secret", "Fingerprint", "Digest"];

        foreach (Type viewModel in viewModels) {
            string[] properties = viewModel.GetProperties().Select(property => property.Name).ToArray();
            properties.Should().NotContain(property =>
                forbidden.Any(name => property.Contains(name, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static ValidationResult[] Validate(object model) {
        var context = new ValidationContext(model);
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(model, context, errors, validateAllProperties: true);
        return errors.ToArray();
    }
}
