using System.Reflection;
using ArtemisBankingPro.Application;
using ArtemisBankingPro.Application.Common.ViewModels;

namespace ArtemisBankingPro.UnitTests.Application.Architecture;

public sealed class ApplicationContractClosureTests {
    private static readonly Assembly ApplicationAssembly = typeof(ServicesRegistration).Assembly;

    [Fact]
    public void ApplicationAssembly_DoesNotReferencePresentationProjects() {
        ApplicationAssembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Where(name =>
                name == "ArtemisBankingPro.Api"
                    || name == "ArtemisBankingPro.WebApp"
                    || name == "ArtemisBankingPro.Functions")
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void ViewModels_DoNotExposeDomainEntitiesOrPresentationTypes() {
        Type[] viewModels = GetViewModelTypes();

        foreach (Type viewModel in viewModels) {
            GetNestedPropertyTypes(viewModel)
                .Where(type =>
                    type.Namespace?.StartsWith("ArtemisBankingPro.Domain", StringComparison.Ordinal)
                        == true
                    || type.Namespace?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                        == true)
                .Should()
                .BeEmpty();
        }
    }

    [Fact]
    public void ViewModels_HaveUniqueShortNamesAcrossFeatures() {
        var duplicates = GetViewModelTypes()
            .GroupBy(type => type.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(type => type.FullName)))
            .ToArray();

        duplicates.Should().BeEmpty();
    }

    [Fact]
    public void ReadViewModels_DoNotExposeSensitiveCardOrCredentialMembers() {
        Type[] readViewModels = [
            typeof(ErrorViewModel),
            typeof(ArtemisBankingPro.Application.Features.Auth.ViewModels.AccessDeniedViewModel),
            typeof(ArtemisBankingPro.Application.Features.Admin.ViewModels.AdminDashboardViewModel),
            typeof(ArtemisBankingPro.Application.Features.Admin.ViewModels.EligibleClientItemViewModel),
            typeof(ArtemisBankingPro.Application.Features.Users.ViewModels.UserListItemViewModel),
            typeof(ArtemisBankingPro.Application.Features.Users.ViewModels.UserDetailViewModel),
            typeof(ArtemisBankingPro.Application.Features.Users.ViewModels.UserMainAccountViewModel),
            typeof(ArtemisBankingPro.Application.Features.Loans.ViewModels.LoanListItemViewModel),
            typeof(ArtemisBankingPro.Application.Features.Loans.ViewModels.LoanDetailViewModel),
            typeof(ArtemisBankingPro.Application.Features.Loans.ViewModels.LoanInstallmentViewModel),
            typeof(ArtemisBankingPro.Application.Features.CreditCard.ViewModels.CreditCardSummaryViewModel),
            typeof(ArtemisBankingPro.Application.Features.CreditCard.ViewModels.CreditCardDetailViewModel),
            typeof(ArtemisBankingPro.Application.Features.CreditCard.ViewModels.CardConsumptionViewModel),
            typeof(ArtemisBankingPro.Application.Features.SavingsAccounts.ViewModels.SavingsAccountSummaryViewModel),
            typeof(ArtemisBankingPro.Application.Features.SavingsAccounts.ViewModels.AccountDetailViewModel),
            typeof(ArtemisBankingPro.Application.Features.SavingsAccounts.ViewModels.AccountTransactionItemViewModel),
            typeof(ArtemisBankingPro.Application.Features.Merchants.ViewModels.MerchantSummaryViewModel),
            typeof(ArtemisBankingPro.Application.Features.Merchants.ViewModels.MerchantDetailViewModel),
            typeof(ArtemisBankingPro.Application.Features.Merchants.ViewModels.MerchantUserViewModel),
            typeof(ArtemisBankingPro.Application.Features.Client.ViewModels.MyProductsViewModel),
            typeof(ArtemisBankingPro.Application.Features.Client.ViewModels.MyAccountViewModel),
            typeof(ArtemisBankingPro.Application.Features.Client.ViewModels.MyLoanViewModel),
            typeof(ArtemisBankingPro.Application.Features.Client.ViewModels.MyCardViewModel),
            typeof(ArtemisBankingPro.Application.Features.Client.ViewModels.MyLoanDetailViewModel),
            typeof(ArtemisBankingPro.Application.Features.Client.ViewModels.MyCardDetailViewModel),
            typeof(ArtemisBankingPro.Application.Features.Client.ViewModels.MyCardConsumptionViewModel),
            typeof(ArtemisBankingPro.Application.Features.Client.ViewModels.BeneficiaryItemViewModel),
            typeof(ArtemisBankingPro.Application.Features.Cashier.ViewModels.CashierDashboardViewModel),
            typeof(ArtemisBankingPro.Application.Features.Cashier.ViewModels.CashierOperationItemViewModel),
            typeof(ArtemisBankingPro.Application.Features.Cashier.ViewModels.TransactionResultViewModel),
            typeof(ArtemisBankingPro.Application.Features.Cashier.ViewModels.ThirdPartyTransferResultViewModel),
        ];
        string[] forbiddenNames = [
            "Pan", "Cvc", "Password", "Jwt", "Secret", "Fingerprint", "Digest", "Token",
        ];

        foreach (Type viewModel in readViewModels) {
            viewModel
                .GetProperties()
                .Select(property => property.Name)
                .Where(property => forbiddenNames.Any(name =>
                    property.Contains(name, StringComparison.OrdinalIgnoreCase)))
                .Should()
                .BeEmpty();
        }
    }

    private static Type[] GetViewModelTypes() => ApplicationAssembly
        .GetTypes()
        .Where(type =>
            type.IsClass
            && !type.IsAbstract
            && type.Name.EndsWith("ViewModel", StringComparison.Ordinal))
        .ToArray();

    private static IEnumerable<Type> GetNestedPropertyTypes(Type type) {
        foreach (PropertyInfo property in type.GetProperties()) {
            yield return property.PropertyType;

            foreach (Type genericArgument in property.PropertyType.GetGenericArguments()) {
                yield return genericArgument;
            }
        }
    }
}
