using System.ComponentModel.DataAnnotations;
using ArtemisBankingPro.Application.Common.Validation;
using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Application.Features.Admin.ViewModels;
using ArtemisBankingPro.Application.Features.Cashier.ViewModels;
using ArtemisBankingPro.Application.Features.Client.ViewModels;
using ArtemisBankingPro.Application.Features.CreditCard.ViewModels;
using ArtemisBankingPro.Application.Features.Loans.ViewModels;
using ArtemisBankingPro.Application.Features.Merchants.ViewModels;
using ArtemisBankingPro.Application.Features.SavingsAccounts.ViewModels;
using ArtemisBankingPro.Application.Features.Users.ViewModels;

namespace ArtemisBankingPro.UnitTests.Application.Common.ViewModels;

public sealed class FeatureViewModelValidationTests {
    [Fact]
    public void ListFilters_RejectUnboundedPresentationInput() {
        var models = new (object Model, string Property)[] {
            (new EligibleClientsViewModel { SelectedClientId = new string('x', 451) }, nameof(EligibleClientsViewModel.SelectedClientId)),
            (new UserListViewModel { Role = new string('x', 51) }, nameof(UserListViewModel.Role)),
            (new LoanListViewModel { Status = new string('x', 51) }, nameof(LoanListViewModel.Status)),
            (new CreditCardListViewModel { Status = new string('x', 51) }, nameof(CreditCardListViewModel.Status)),
            (new SavingsAccountListViewModel { Status = new string('x', 51) }, nameof(SavingsAccountListViewModel.Status)),
            (new SavingsAccountListViewModel { Type = new string('x', 51) }, nameof(SavingsAccountListViewModel.Type)),
            (new MerchantListViewModel { Status = new string('x', 51) }, nameof(MerchantListViewModel.Status)),
            (new MyAccountTransactionsViewModel { TransactionType = new string('x', 51) }, nameof(MyAccountTransactionsViewModel.TransactionType)),
            (new CashierOperationListViewModel { OperationType = new string('x', 51) }, nameof(CashierOperationListViewModel.OperationType)),
        };

        foreach ((object model, string property) in models) {
            ValidationResult[] errors = Validate(model);

            errors.Should().Contain(error => error.MemberNames.Contains(property));
        }
    }

    [Fact]
    public void IdentificationViewModels_UseTheSqlColumnLimitAndContractMessage() {
        string tooLong = new(
            '1',
            IdentityValidationLimits.IdentificationMaxLength + 1
        );
        var models = new object[] {
            new CreateUserViewModel { Identification = tooLong },
            new UpdateUserViewModel { Identification = tooLong },
            new CreateCommerceUserViewModel { Identification = tooLong },
            new AssignCommerceUserViewModel { Identification = tooLong },
            new EligibleClientsViewModel { Identification = tooLong },
            new LoanListViewModel { Identification = tooLong },
            new CreditCardListViewModel { Identification = tooLong },
            new SavingsAccountListViewModel { Identification = tooLong },
        };

        foreach (object model in models) {
            Validate(model).Should().Contain(error =>
                error.MemberNames.Contains("Identification")
                && error.ErrorMessage == IdentityValidationLimits.IdentificationMaxLengthMessage
            );
        }
    }

    [Fact]
    public void ResourceActionViewModels_RequireRouteIdentifiers() {
        ValidationResult[] userErrors = Validate(new ChangeUserStatusViewModel());
        ValidationResult[] merchantErrors = Validate(new ChangeMerchantStatusViewModel());

        userErrors.Should().Contain(error =>
            error.MemberNames.Contains(nameof(ChangeUserStatusViewModel.UserId)));
        merchantErrors.Should().Contain(error =>
            error.MemberNames.Contains(nameof(ChangeMerchantStatusViewModel.MerchantId)));
    }

    [Fact]
    public void FinancialForms_RejectNonPositiveAmountsAtPresentationBoundary() {
        var models = new object[] {
            new AssignCreditCardViewModel { CreditLimit = 0m },
            new UpdateCardLimitViewModel { NewLimit = 0m },
            new AssignSecondaryAccountViewModel { InitialAmount = -1m },
            new CreateLoanViewModel { CapitalAmount = 0m, TermMonths = 12, AnnualInterestRate = 0m },
            new UpdateLoanRateViewModel { AnnualInterestRate = -1m },
            new DepositViewModel { AccountNumber = "000000001", Amount = 0m },
            new WithdrawalViewModel { AccountNumber = "000000001", Amount = 0m },
            new CardPaymentViewModel { CardId = 1, AccountNumber = "000000001", Amount = 0m },
            new LoanPaymentViewModel { LoanId = 1, AccountNumber = "000000001", Amount = 0m },
            new ThirdPartyTransferViewModel {
                SourceAccountNumber = "000000001",
                DestinationAccountNumber = "000000002",
                Amount = 0m,
            },
            new ExpressTransactionViewModel {
                SourceAccountNumber = "000000001",
                DestinationAccountNumber = "000000002",
                Amount = 0m,
            },
            new BeneficiaryTransferViewModel {
                BeneficiaryId = 1,
                SourceAccountNumber = "000000001",
                Amount = 0m,
            },
            new ClientCardPaymentViewModel {
                CardId = 1,
                AccountNumber = "000000001",
                Amount = 0m,
            },
            new ClientLoanPaymentViewModel {
                LoanId = 1,
                AccountNumber = "000000001",
                Amount = 0m,
            },
            new CashAdvanceViewModel {
                CardId = 1,
                DestinationAccountNumber = "000000001",
                Amount = 0m,
            },
            new OwnAccountsTransferViewModel {
                SourceAccountNumber = "000000001",
                DestinationAccountNumber = "000000002",
                Amount = 0m,
            },
        };

        foreach (object model in models) {
            Validate(model).Should().NotBeEmpty();
        }
    }

    [Fact]
    public void FinancialFilters_RejectInvalidAccountFormatAndDateOrder() {
        MyAccountTransactionsViewModel invalidAccount = new() {
            AccountNumber = "123",
        };
        MyAccountTransactionsViewModel account = new() {
            AccountNumber = "000000001",
            DateFrom = new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero),
            DateTo = new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero),
        };
        CashierOperationListViewModel operations = new() {
            DateFrom = new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero),
            DateTo = new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero),
        };

        ValidationResult[] invalidAccountErrors = Validate(invalidAccount);
        ValidationResult[] accountErrors = Validate(account);
        ValidationResult[] operationErrors = Validate(operations);

        invalidAccountErrors.Should().Contain(error =>
            error.MemberNames.Contains(nameof(MyAccountTransactionsViewModel.AccountNumber)));
        accountErrors.Should().Contain(error =>
            error.MemberNames.Contains(nameof(MyAccountTransactionsViewModel.DateFrom))
            && error.MemberNames.Contains(nameof(MyAccountTransactionsViewModel.DateTo)));
        operationErrors.Should().Contain(error =>
            error.MemberNames.Contains(nameof(CashierOperationListViewModel.DateFrom))
            && error.MemberNames.Contains(nameof(CashierOperationListViewModel.DateTo)));
    }

    [Fact]
    public void Product_status_filters_select_all_when_searching_by_client_without_status() {
        CreditCardListViewModel cards = new() {
            Identification = "00100000001",
            StatusOptions = CreditCardListViewModel.BuildStatusOptions(null, true),
        };
        LoanListViewModel loans = new() {
            Identification = "00100000001",
            StatusOptions = LoanListViewModel.BuildStatusOptions(null, true),
        };
        SavingsAccountListViewModel accounts = new() {
            Identification = "00100000001",
            StatusOptions = SavingsAccountListViewModel.BuildStatusOptions(null, true),
        };

        cards.StatusOptions.Single(option => option.Text == "Todas").IsSelected.Should().BeTrue();
        loans.StatusOptions.Single(option => option.Text == "Todos").IsSelected.Should().BeTrue();
        accounts.StatusOptions.Single(option => option.Text == "Todas").IsSelected.Should().BeTrue();
    }

    [Fact]
    public void PresentationValidation_DoesNotReplaceServerAuthoritativeRules() {
        CreateLoanViewModel model = new() {
            CapitalAmount = 1m,
            TermMonths = 12,
            AnnualInterestRate = 0m,
        };

        Validate(model).Should().BeEmpty();
        typeof(CreateLoanViewModel).GetProperties()
            .Select(property => property.Name)
            .Should().NotContain("CustomerUserId");
        typeof(HighRiskLoanConfirmationViewModel).GetProperties()
            .Select(property => property.Name)
            .Should().Contain(nameof(ConfirmationViewModel.ConfirmationToken));
    }

    private static ValidationResult[] Validate(object model) {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            errors,
            validateAllProperties: true
        );
        return errors.ToArray();
    }
}
