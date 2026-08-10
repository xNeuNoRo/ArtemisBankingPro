using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Common.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Domain.Operations.Errors;
using ArtemisBankingPro.Domain.Operations.Events;

namespace ArtemisBankingPro.Domain.Operations.Entities;

/// <summary>
/// Representa una operación financiera realizada por un cliente, como un retiro, transferencia, pago de tarjeta o préstamo.
/// Contiene información sobre el tipo de operación, estado, montos, usuario que la inició, fecha y detalles asociados (transacciones de cuenta y consumo de tarjeta).
/// </summary>
public sealed class FinancialOperation : AggregateRoot<Guid> {
    private readonly List<AccountTransaction> _accountTransactions = [];

    private FinancialOperation() { }

    private FinancialOperation(
        Guid id,
        FinancialOperationKind kind,
        FinancialOperationStatus status,
        Money requestedAmount,
        Money appliedAmount,
        Money interestAmount,
        string initiatedByUserId,
        DateTimeOffset occurredAt,
        string? rejectionCode,
        int? creditCardId,
        LoanNumber? loanNumber,
        int? merchantId,
        IEnumerable<AccountTransaction> accountTransactions,
        CardConsumption? cardConsumption
    ) {
        Id = id;
        Kind = kind;
        Status = status;
        RequestedAmount = requestedAmount;
        AppliedAmount = appliedAmount;
        InterestAmount = interestAmount;
        InitiatedByUserId = initiatedByUserId;
        OccurredAt = occurredAt;
        RejectionCode = rejectionCode;
        CreditCardId = creditCardId;
        LoanNumber = loanNumber;
        MerchantId = merchantId;
        _accountTransactions.AddRange(accountTransactions);
        CardConsumption = cardConsumption;
    }

    public FinancialOperationKind Kind { get; private set; }

    public FinancialOperationStatus Status { get; private set; }

    public Money RequestedAmount { get; private set; } = Money.Zero;

    public Money AppliedAmount { get; private set; } = Money.Zero;

    public Money InterestAmount { get; private set; } = Money.Zero;

    public string InitiatedByUserId { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public string? RejectionCode { get; private set; }

    public int? CreditCardId { get; private set; }

    public LoanNumber? LoanNumber { get; private set; }

    public int? MerchantId { get; private set; }

    public IReadOnlyCollection<AccountTransaction> AccountTransactions =>
        _accountTransactions.AsReadOnly();

    public CardConsumption? CardConsumption { get; private set; }

    public static Result<FinancialOperation> Approve(
        Guid id,
        FinancialOperationKind kind,
        Money requestedAmount,
        Money appliedAmount,
        Money interestAmount,
        string initiatedByUserId,
        DateTimeOffset occurredAt,
        IReadOnlyCollection<AccountTransactionDetails> accountTransactions,
        CardConsumptionDetails? cardConsumption = null,
        int? creditCardId = null,
        LoanNumber? loanNumber = null,
        int? merchantId = null
    ) {
        Result<FinancialOperation> result = Create(
            id,
            kind,
            FinancialOperationStatus.Approved,
            requestedAmount,
            appliedAmount,
            interestAmount,
            initiatedByUserId,
            occurredAt,
            null,
            creditCardId,
            loanNumber,
            merchantId,
            accountTransactions,
            cardConsumption
        );

        if (result.IsSuccess) {
            result.Value.RaiseDomainEvent(new FinancialOperationApprovedEvent(id, kind));
        }

        return result;
    }

    public static Result<FinancialOperation> Reject(
        Guid id,
        FinancialOperationKind kind,
        Money requestedAmount,
        Money interestAmount,
        string initiatedByUserId,
        DateTimeOffset occurredAt,
        string rejectionCode,
        IReadOnlyCollection<AccountTransactionDetails> accountTransactions,
        CardConsumptionDetails? cardConsumption = null,
        int? creditCardId = null,
        LoanNumber? loanNumber = null,
        int? merchantId = null
    ) =>
        Create(
            id,
            kind,
            FinancialOperationStatus.Rejected,
            requestedAmount,
            Money.Zero,
            interestAmount,
            initiatedByUserId,
            occurredAt,
            rejectionCode,
            creditCardId,
            loanNumber,
            merchantId,
            accountTransactions,
            cardConsumption
        );

    private static Result<FinancialOperation> Create(
        Guid id,
        FinancialOperationKind kind,
        FinancialOperationStatus status,
        Money requestedAmount,
        Money appliedAmount,
        Money interestAmount,
        string initiatedByUserId,
        DateTimeOffset occurredAt,
        string? rejectionCode,
        int? creditCardId,
        LoanNumber? loanNumber,
        int? merchantId,
        IReadOnlyCollection<AccountTransactionDetails> accountTransactionDetails,
        CardConsumptionDetails? cardConsumptionDetails
    ) {
        Result basicValidation = ValidateBasicData(
            id,
            kind,
            status,
            requestedAmount,
            appliedAmount,
            interestAmount,
            initiatedByUserId,
            rejectionCode
        );
        if (basicValidation.IsFailure) {
            return Result.Failure<FinancialOperation>(basicValidation.Error!);
        }

        Result detailValidation = ValidateDetails(
            kind,
            status,
            requestedAmount,
            appliedAmount,
            interestAmount,
            creditCardId,
            loanNumber,
            merchantId,
            accountTransactionDetails,
            cardConsumptionDetails
        );
        if (detailValidation.IsFailure) {
            return Result.Failure<FinancialOperation>(detailValidation.Error!);
        }

        List<AccountTransaction> accountTransactions = new(accountTransactionDetails.Count);
        foreach (AccountTransactionDetails details in accountTransactionDetails) {
            Result<AccountTransaction> transaction = AccountTransaction.Create(id, details);
            if (transaction.IsFailure) {
                return Result.Failure<FinancialOperation>(transaction.Error!);
            }

            accountTransactions.Add(transaction.Value);
        }

        CardConsumption? cardConsumption = null;
        if (cardConsumptionDetails is not null) {
            Result<CardConsumption> consumption = CardConsumption.Create(
                id,
                cardConsumptionDetails
            );
            if (consumption.IsFailure) {
                return Result.Failure<FinancialOperation>(consumption.Error!);
            }

            cardConsumption = consumption.Value;
        }

        return Result.Success(
            new FinancialOperation(
                id,
                kind,
                status,
                requestedAmount,
                appliedAmount,
                interestAmount,
                initiatedByUserId,
                occurredAt,
                rejectionCode,
                creditCardId,
                loanNumber,
                merchantId,
                accountTransactions,
                cardConsumption
            )
        );
    }

    private static Result ValidateBasicData(
        Guid id,
        FinancialOperationKind kind,
        FinancialOperationStatus status,
        Money requestedAmount,
        Money appliedAmount,
        Money interestAmount,
        string initiatedByUserId,
        string? rejectionCode
    ) {
        if (id == Guid.Empty) {
            return Result.Failure(OperationErrors.InvalidId);
        }

        if (!Enum.IsDefined(kind)) {
            return Result.Failure(OperationErrors.InvalidKind);
        }

        if (requestedAmount is null || appliedAmount is null || interestAmount is null) {
            return Result.Failure(OperationErrors.InvalidAmountEquation);
        }

        if (string.IsNullOrWhiteSpace(initiatedByUserId)) {
            return Result.Failure(OperationErrors.InvalidActor);
        }

        bool expectsNoAmount = kind == FinancialOperationKind.CardCancelled;

        if (!expectsNoAmount && requestedAmount.Amount <= 0m) {
            return Result.Failure(OperationErrors.InvalidRequestedAmount);
        }

        if (expectsNoAmount && requestedAmount != Money.Zero) {
            return Result.Failure(OperationErrors.InvalidAmountEquation);
        }

        if (status == FinancialOperationStatus.Approved
            && appliedAmount.Amount <= 0m
            && !expectsNoAmount) {
            return Result.Failure(OperationErrors.InvalidAppliedAmount);
        }

        if (expectsNoAmount && appliedAmount != Money.Zero) {
            return Result.Failure(OperationErrors.InvalidAmountEquation);
        }

        if (status == FinancialOperationStatus.Rejected && appliedAmount != Money.Zero) {
            return Result.Failure(OperationErrors.RejectedOperationAppliedFunds);
        }

        if (status == FinancialOperationStatus.Rejected && string.IsNullOrWhiteSpace(rejectionCode)) {
            return Result.Failure(OperationErrors.MissingRejectionCode);
        }

        bool isPayment =
            kind is FinancialOperationKind.CreditCardPayment or FinancialOperationKind.LoanPayment;
        if (
            status == FinancialOperationStatus.Approved
            && (isPayment ? appliedAmount > requestedAmount : appliedAmount != requestedAmount)
        ) {
            return Result.Failure(OperationErrors.InvalidAmountEquation);
        }

        if (
            kind == FinancialOperationKind.CashAdvance
                ? interestAmount.Amount <= 0m
                : interestAmount != Money.Zero
        ) {
            return Result.Failure(OperationErrors.InvalidAmountEquation);
        }

        return Result.Success();
    }

    private static Result ValidateDetails(
        FinancialOperationKind kind,
        FinancialOperationStatus status,
        Money requestedAmount,
        Money appliedAmount,
        Money interestAmount,
        int? creditCardId,
        LoanNumber? loanNumber,
        int? merchantId,
        IReadOnlyCollection<AccountTransactionDetails>? accountTransactions,
        CardConsumptionDetails? cardConsumption
    ) {
        if (accountTransactions is null) {
            return Result.Failure(OperationErrors.InvalidDetails);
        }

        int expectedAccountTransactions = GetExpectedAccountTransactionCount(kind, status);
        if (
            expectedAccountTransactions < 0
            || accountTransactions.Count != expectedAccountTransactions
        ) {
            return Result.Failure(OperationErrors.InvalidDetails);
        }

        bool requiresConsumption =
            kind is FinancialOperationKind.CashAdvance or FinancialOperationKind.HermesPayment;
        if (requiresConsumption != (cardConsumption is not null)) {
            return Result.Failure(OperationErrors.InvalidDetails);
        }

        Result referenceValidation = ValidateProductReferences(
            kind,
            creditCardId,
            loanNumber,
            merchantId,
            cardConsumption
        );
        if (referenceValidation.IsFailure) {
            return referenceValidation;
        }

        Money expectedMovementAmount =
            status == FinancialOperationStatus.Approved ? appliedAmount : requestedAmount;
        if (accountTransactions.Any(details => details.Amount != expectedMovementAmount)) {
            return Result.Failure(OperationErrors.InvalidAmountEquation);
        }

        if (accountTransactions.Count == 2 && !IsBalancedTransfer(accountTransactions)) {
            return Result.Failure(OperationErrors.UnbalancedTransfer);
        }

        if (
            accountTransactions.Count == 1
            && accountTransactions.Single().Direction != GetExpectedDirection(kind)
        ) {
            return Result.Failure(OperationErrors.InvalidDetails);
        }

        if (cardConsumption is not null) {
            Money baseAmount = requestedAmount;
            if (
                kind == FinancialOperationKind.CashAdvance
                && status == FinancialOperationStatus.Approved
            ) {
                baseAmount = appliedAmount;
            }

            Money expectedConsumptionAmount =
                kind == FinancialOperationKind.CashAdvance
                    ? baseAmount.Add(interestAmount)
                    : baseAmount;
            if (cardConsumption.Amount != expectedConsumptionAmount) {
                return Result.Failure(OperationErrors.InvalidAmountEquation);
            }
        }

        return Result.Success();
    }

    private static Result ValidateProductReferences(
        FinancialOperationKind kind,
        int? creditCardId,
        LoanNumber? loanNumber,
        int? merchantId,
        CardConsumptionDetails? cardConsumption
    ) {
        bool requiresCard =
            kind
            is FinancialOperationKind.CreditCardPayment
                or FinancialOperationKind.CashAdvance
                or FinancialOperationKind.HermesPayment
                or FinancialOperationKind.CardCancelled;
        bool requiresLoan =
            kind is FinancialOperationKind.LoanDisbursement or FinancialOperationKind.LoanPayment;
        bool requiresMerchant = kind == FinancialOperationKind.HermesPayment;

        if (
            requiresCard != (creditCardId is > 0)
            || requiresLoan != (loanNumber is not null)
            || requiresMerchant != (merchantId is > 0)
        ) {
            return Result.Failure(OperationErrors.InvalidProductReference);
        }

        if (
            !requiresCard && creditCardId is not null
            || !requiresLoan && loanNumber is not null
            || !requiresMerchant && merchantId is not null
        ) {
            return Result.Failure(OperationErrors.InvalidProductReference);
        }

        if (
            cardConsumption is not null
            && (
                cardConsumption.CreditCardId != creditCardId
                || cardConsumption.MerchantId != merchantId
                || kind == FinancialOperationKind.CashAdvance
                    && cardConsumption.Type != ConsumptionType.CashAdvance
                || kind == FinancialOperationKind.HermesPayment
                    && cardConsumption.Type != ConsumptionType.Purchase
            )
        ) {
            return Result.Failure(OperationErrors.InvalidProductReference);
        }

        return Result.Success();
    }

    private static int GetExpectedAccountTransactionCount(
        FinancialOperationKind kind,
        FinancialOperationStatus status
    ) {
        if (status == FinancialOperationStatus.Approved) {
            if (
                kind
                    is FinancialOperationKind.ExpressTransfer
                        or FinancialOperationKind.BeneficiaryTransfer
                        or FinancialOperationKind.OwnAccountTransfer
                        or FinancialOperationKind.CashierTransfer
                        or FinancialOperationKind.SecondaryAccountClosureTransfer
            ) {
                return 2;
            }

            return kind == FinancialOperationKind.CardCancelled ? 0 : 1;
        }

        return kind switch {
            FinancialOperationKind.Withdrawal
            or FinancialOperationKind.ExpressTransfer
            or FinancialOperationKind.BeneficiaryTransfer
            or FinancialOperationKind.OwnAccountTransfer
            or FinancialOperationKind.CashierTransfer
            or FinancialOperationKind.CreditCardPayment
            or FinancialOperationKind.LoanPayment => 1,
            FinancialOperationKind.CashAdvance or FinancialOperationKind.HermesPayment => 0,
            _ => -1,
        };
    }

    private static TransactionDirection GetExpectedDirection(FinancialOperationKind kind) =>
        kind
            is FinancialOperationKind.InitialFunding
                or FinancialOperationKind.AdministrativeFunding
                or FinancialOperationKind.LoanDisbursement
                or FinancialOperationKind.Deposit
                or FinancialOperationKind.CashAdvance
                or FinancialOperationKind.HermesPayment
            ? TransactionDirection.Credit
            : TransactionDirection.Debit;

    private static bool IsBalancedTransfer(
        IReadOnlyCollection<AccountTransactionDetails> transactions
    ) {
        AccountTransactionDetails[] debits = transactions
            .Where(details => details.Direction == TransactionDirection.Debit)
            .ToArray();
        AccountTransactionDetails[] credits = transactions
            .Where(details => details.Direction == TransactionDirection.Credit)
            .ToArray();

        return debits.Length == 1
            && credits.Length == 1
            && debits[0].Amount == credits[0].Amount
            && debits[0].AccountNumber != credits[0].AccountNumber;
    }
}
