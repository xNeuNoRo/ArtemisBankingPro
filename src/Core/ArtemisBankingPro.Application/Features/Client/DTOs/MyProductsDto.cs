namespace ArtemisBankingPro.Application.Features.Client.DTOs;

public sealed record MyProductsDto(
    List<MyAccountDto> Accounts,
    List<MyLoanDto> Loans,
    List<MyCardDto> Cards
);

public sealed record MyAccountDto(string AccountNumber, decimal Balance, string Type);

public sealed record MyLoanDto(
    string LoanNumber,
    decimal ApprovedPrincipal,
    int TotalInstallments,
    int PaidInstallments,
    decimal OutstandingAmount,
    decimal AnnualRate,
    int TermMonths,
    bool IsDelinquent
) {
    /// <summary>
    /// Internal resource identifier used by authorized detail and payment flows.
    /// It is not a substitute for the ownership check performed by the handler.
    /// </summary>
    public int LoanId { get; init; }
}

public sealed record MyCardDto(
    string LastFour,
    decimal CreditLimit,
    decimal AvailableCredit,
    decimal CurrentDebt,
    string Expiration
) {
    /// <summary>
    /// Internal resource identifier used by authorized detail and payment flows.
    /// It is not a substitute for the ownership check performed by the handler.
    /// </summary>
    public int CardId { get; init; }
}
