using System.ComponentModel.DataAnnotations;
using ArtemisBankingPro.Application.Common.Mapping;
using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Application.Features.Cashier.Mapping;
using ArtemisBankingPro.Application.Features.Cashier.ViewModels;
using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Application.Features.Client.Mapping;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Features.Client.ViewModels;
using MapsterMapper;

namespace ArtemisBankingPro.UnitTests.Application.Features.ClientCashier.ViewModels;

public sealed class ClientCashierViewModelTests {
    private static readonly ServiceMapper Mapper = new(null!, MapsterConfig.Create());

    [Fact]
    public void MapsterConfiguration_CompilesClientAndCashierMappings() {
        Action compile = () => MapsterConfig.Create().Compile();

        compile.Should().NotThrow();
    }

    [Fact]
    public void ClientFinancialMapping_UsesServerIssuedIdempotencyKey() {
        ExpressTransactionViewModel source = new() {
            SourceAccountNumber = "000000001",
            DestinationAccountNumber = "000000002",
            Amount = 125.50m,
        };

        ProcessExpressTransactionCommand command = ClientMappingRegister.ToExpressTransactionCommand(
            source,
            Mapper,
            "server-confirmation-1"
        );

        command.SourceAccountNumber.Should().Be("000000001");
        command.DestinationAccountNumber.Should().Be("000000002");
        command.Amount.Should().Be(125.50m);
        command.IdempotencyKey.Should().Be("server-confirmation-1");
    }

    [Fact]
    public void ClientFinancialMappings_TranslateAllCommandForms() {
        AddBeneficiaryCommand addBeneficiary = ClientMappingRegister.ToAddBeneficiaryCommand(
            new AddBeneficiaryViewModel { DestinationAccountNumber = "000000002" },
            Mapper,
            "client-add"
        );
        RemoveBeneficiaryCommand removeBeneficiary = ClientMappingRegister.ToRemoveBeneficiaryCommand(
            new RemoveBeneficiaryViewModel { BeneficiaryId = 999 },
            3,
            "client-remove"
        );
        ProcessBeneficiaryTransferCommand beneficiaryTransfer =
            ClientMappingRegister.ToBeneficiaryTransferCommand(
                new BeneficiaryTransferViewModel {
                    BeneficiaryId = 3,
                    SourceAccountNumber = "000000001",
                    Amount = 10m,
                },
                Mapper,
                "client-beneficiary-transfer"
            );
        ProcessOwnAccountsTransferCommand ownTransfer =
            ClientMappingRegister.ToOwnAccountsTransferCommand(
                new OwnAccountsTransferViewModel {
                    SourceAccountNumber = "000000001",
                    DestinationAccountNumber = "000000002",
                    Amount = 20m,
                },
                Mapper,
                "client-own-transfer"
            );
        ProcessClientCardPaymentCommand cardPayment = ClientMappingRegister.ToClientCardPaymentCommand(
            new ClientCardPaymentViewModel {
                CardId = 4,
                AccountNumber = "000000001",
                Amount = 30m,
            },
            Mapper,
            "client-card-payment"
        );
        ProcessClientLoanPaymentCommand loanPayment = ClientMappingRegister.ToClientLoanPaymentCommand(
            new ClientLoanPaymentViewModel {
                LoanId = 5,
                AccountNumber = "000000001",
                Amount = 40m,
            },
            Mapper,
            "client-loan-payment"
        );
        ProcessCashAdvanceCommand cashAdvance = ClientMappingRegister.ToCashAdvanceCommand(
            new CashAdvanceViewModel {
                CardId = 4,
                DestinationAccountNumber = "000000001",
                Amount = 50m,
            },
            Mapper,
            "client-cash-advance"
        );

        addBeneficiary.IdempotencyKey.Should().Be("client-add");
        removeBeneficiary.BeneficiaryId.Should().Be(3);
        removeBeneficiary.IdempotencyKey.Should().Be("client-remove");
        beneficiaryTransfer.IdempotencyKey.Should().Be("client-beneficiary-transfer");
        ownTransfer.IdempotencyKey.Should().Be("client-own-transfer");
        cardPayment.IdempotencyKey.Should().Be("client-card-payment");
        loanPayment.IdempotencyKey.Should().Be("client-loan-payment");
        cashAdvance.IdempotencyKey.Should().Be("client-cash-advance");
    }

    [Fact]
    public void ClientProductMapping_PreservesInternalResourceIdsWithoutSensitiveCardData() {
        MyProductsDto source = new(
            [new MyAccountDto("000000001", 100m, "Principal")],
            [new MyLoanDto("000000001", 1_000m, 12, 2, 800m, 12m, 12, false) {
                LoanId = 41,
            }],
            [new MyCardDto("1234", 5_000m, 4_000m, 1_000m, "12/28") {
                CardId = 17,
            }]
        );

        MyProductsViewModel result = ClientMappingRegister.ToProductsViewModel(source, Mapper);

        result.Loans.Should().ContainSingle().Which.LoanId.Should().Be(41);
        result.Cards.Should().ContainSingle().Which.CardId.Should().Be(17);
        result.Cards.Should().ContainSingle().Which.LastFour.Should().Be("1234");
    }

    [Fact]
    public void ClientDetailMappings_DoNotTrustPostedRouteIdentifiers() {
        GetMyLoanDetailQuery loan = Mapper.Map<GetMyLoanDetailQuery>(
            new MyLoanDetailViewModel { LoanId = 999 }
        );
        GetMyCardDetailQuery card = Mapper.Map<GetMyCardDetailQuery>(
            new MyCardDetailViewModel { CardId = 999 }
        );
        GetMyAccountTransactionsQuery account = Mapper.Map<GetMyAccountTransactionsQuery>(
            new MyAccountTransactionsViewModel { AccountNumber = "999999999" }
        );

        loan.LoanId.Should().Be(0);
        card.CardId.Should().Be(0);
        account.AccountNumber.Should().BeEmpty();
    }

    [Fact]
    public void CashierFinancialMapping_UsesServerIssuedIdempotencyKey() {
        CardPaymentViewModel source = new() {
            CardId = 17,
            AccountNumber = "000000001",
            Amount = 300m,
        };

        ProcessCardPaymentCommand command = CashierMappingRegister.ToCardPaymentCommand(
            source,
            Mapper,
            "server-confirmation-2"
        );

        command.CardId.Should().Be(17);
        command.AccountNumber.Should().Be("000000001");
        command.Amount.Should().Be(300m);
        command.IdempotencyKey.Should().Be("server-confirmation-2");
    }

    [Fact]
    public void CashierFinancialMappings_TranslateAllCommandForms() {
        DepositViewModel depositSource = new() {
            AccountNumber = "000000001",
            Amount = 10m,
        };
        WithdrawalViewModel withdrawalSource = new() {
            AccountNumber = "000000001",
            Amount = 20m,
        };
        LoanPaymentViewModel loanSource = new() {
            LoanId = 5,
            AccountNumber = "000000001",
            Amount = 30m,
        };
        ThirdPartyTransferViewModel transferSource = new() {
            SourceAccountNumber = "000000001",
            DestinationAccountNumber = "000000002",
            Amount = 40m,
        };

        ProcessDepositCommand deposit = CashierMappingRegister.ToDepositCommand(
            depositSource,
            Mapper,
            "cashier-deposit"
        );
        ProcessWithdrawalCommand withdrawal = CashierMappingRegister.ToWithdrawalCommand(
            withdrawalSource,
            Mapper,
            "cashier-withdrawal"
        );
        ProcessLoanPaymentCommand loan = CashierMappingRegister.ToLoanPaymentCommand(
            loanSource,
            Mapper,
            "cashier-loan"
        );
        ProcessThirdPartyTransferCommand transfer = CashierMappingRegister.ToThirdPartyTransferCommand(
            transferSource,
            Mapper,
            "cashier-transfer"
        );

        deposit.IdempotencyKey.Should().Be("cashier-deposit");
        withdrawal.IdempotencyKey.Should().Be("cashier-withdrawal");
        loan.IdempotencyKey.Should().Be("cashier-loan");
        transfer.IdempotencyKey.Should().Be("cashier-transfer");
    }

    [Fact]
    public void CashierResponseMapping_PreservesNotificationWarningForPresentation() {
        CashierOperationResponse source = new(
            Guid.NewGuid(),
            "000000001",
            "000000002",
            250m,
            DateTimeOffset.UtcNow,
            "APROBADO",
            NotificationWarning: "La operación fue realizada, pero falló una notificación."
        );

        TransactionResultViewModel result = Mapper.Map<TransactionResultViewModel>(source);

        result.Amount.Should().Be(250m);
        result.NotificationWarning.Should().NotBeNullOrWhiteSpace();
        result.OperationId.Should().Be(source.OperationId);
    }

    [Fact]
    public void FinancialFormValidation_RejectsInvalidAmountAccountAndSameAccount() {
        OwnAccountsTransferViewModel source = new() {
            SourceAccountNumber = "000000001",
            DestinationAccountNumber = "000000001",
            Amount = 0m,
        };

        ValidationResult[] errors = Validate(source);

        errors.Select(error => error.ErrorMessage)
            .Should()
            .Contain("El monto a transferir debe ser mayor que cero.");
        errors.Select(error => error.ErrorMessage)
            .Should()
            .Contain("La cuenta origen y la cuenta destino no pueden ser la misma.");
    }

    [Fact]
    public void ReadViewModels_DoNotExposeSensitiveCardOrCredentialFields() {
        Type[] viewModels = [
            typeof(MyProductsViewModel),
            typeof(MyCardViewModel),
            typeof(MyCardDetailViewModel),
            typeof(MyCardConsumptionViewModel),
            typeof(CashierOperationItemViewModel),
            typeof(TransactionResultViewModel),
            typeof(ThirdPartyTransferResultViewModel),
        ];
        string[] forbidden = [
            "Pan", "Cvc", "Password", "Jwt", "Secret", "Fingerprint", "Digest",
        ];

        foreach (Type viewModel in viewModels) {
            viewModel
                .GetProperties()
                .Select(property => property.Name)
                .Should()
                .NotContain(property => forbidden.Any(name =>
                    property.Contains(name, StringComparison.OrdinalIgnoreCase)));
        }
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
