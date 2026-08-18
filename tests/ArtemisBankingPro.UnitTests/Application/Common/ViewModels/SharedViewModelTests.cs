using System.ComponentModel.DataAnnotations;
using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Domain.Common.Pagination;

namespace ArtemisBankingPro.UnitTests.Application.Common.ViewModels;

public sealed class SharedViewModelTests {
    [Fact]
    public void BaseViewModel_ContainsOnlyPresentationContext() {
        string[] forbiddenNames =
        [
            "Balance",
            "Debt",
            "Limit",
            "Amount",
            "Ownership",
            "Status",
            "Timestamp",
            "Pan",
            "Cvc",
            "Token",
        ];

        typeof(ProbeViewModel)
            .GetProperties()
            .Select(property => property.Name)
            .Should()
            .NotContain(propertyName =>
                forbiddenNames.Any(forbidden =>
                    propertyName.Contains(forbidden, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void BaseViewModel_ExposesContextWithoutFinancialState() {
        var viewModel = new ProbeViewModel {
            PageTitle = "Panel",
            CurrentUserId = "user-1",
            CurrentUserName = "cliente",
            CurrentUserRole = "Cliente",
            IsAuthenticated = true,
            ActiveNavigationItem = "dashboard",
            Messages = ["Sesión iniciada"],
        };

        viewModel.PageTitle.Should().Be("Panel");
        viewModel.CurrentUserRole.Should().Be("Cliente");
        viewModel.Messages.Should().ContainSingle("Sesión iniciada");
    }

    [Fact]
    public void PaginationViewModel_UsesProjectDefaultsAndCalculatesNavigation() {
        var viewModel = new PaginationViewModel { TotalItems = 41 };

        viewModel.Page.Should().Be(PageRequest.DefaultPage);
        viewModel.PageSize.Should().Be(PageRequest.DefaultPageSize);
        viewModel.TotalPages.Should().Be(3);
        viewModel.HasPreviousPage.Should().BeFalse();
        viewModel.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public void PaginationViewModel_EmptyResultHasNoPages() {
        var viewModel = new PaginationViewModel { TotalItems = 0 };

        viewModel.TotalPages.Should().Be(0);
        viewModel.HasPreviousPage.Should().BeFalse();
        viewModel.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public void SelectOptionViewModel_PreservesLeadingZeroes() {
        var option = new SelectOptionViewModel {
            Value = "000000123",
            Text = "Cuenta principal",
            IsSelected = true,
        };

        option.Value.Should().Be("000000123");
        option.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void ConfirmationViewModel_RequiresServerIssuedToken() {
        var viewModel = new ConfirmationViewModel();

        var validationContext = new ValidationContext(viewModel);
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(viewModel, validationContext, errors, validateAllProperties: true)
            .Should()
            .BeFalse();

        errors.Should().Contain(error =>
            error.MemberNames.Contains(nameof(ConfirmationViewModel.ConfirmationToken)));
    }

    [Fact]
    public void ConfirmationViewModel_ValidTokenPassesPresentationValidation() {
        var viewModel = new ConfirmationViewModel {
            ConfirmationToken = "server-issued-single-use-token",
        };

        var validationContext = new ValidationContext(viewModel);
        var errors = new List<ValidationResult>();

        Validator.TryValidateObject(viewModel, validationContext, errors, validateAllProperties: true)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ErrorViewModel_ContainsSafePresentationDataOnly() {
        var viewModel = new ErrorViewModel {
            RequestId = "trace-123",
            StatusCode = 500,
            Title = "Error interno",
            Message = "No fue posible completar la solicitud.",
        };

        viewModel.ShowRequestId.Should().BeTrue();
        typeof(ErrorViewModel)
            .GetProperties()
            .Select(property => property.Name)
            .Should()
            .NotContain(propertyName =>
                propertyName.Contains("Exception", StringComparison.OrdinalIgnoreCase)
                || propertyName.Contains("StackTrace", StringComparison.OrdinalIgnoreCase)
                || propertyName.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                || propertyName.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ProbeViewModel : BaseViewModel;
}
