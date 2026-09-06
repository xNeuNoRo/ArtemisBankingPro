namespace ArtemisBankingPro.Domain.Operations.Enums;

public enum FinancialOperationKind {
    InitialFunding = 1,
    AdministrativeFunding = 2,
    LoanDisbursement = 3,
    Deposit = 4,
    Withdrawal = 5,
    ExpressTransfer = 6,
    BeneficiaryTransfer = 7,
    OwnAccountTransfer = 8,
    CashierTransfer = 9,
    CreditCardPayment = 10,
    LoanPayment = 11,
    CashAdvance = 12,
    HermesPayment = 13,
    SecondaryAccountClosureTransfer = 14,
    CardCancelled = 15,
    CardLimitChanged = 16,
    CardAssigned = 17,
    AccountCancelled = 18,
}
