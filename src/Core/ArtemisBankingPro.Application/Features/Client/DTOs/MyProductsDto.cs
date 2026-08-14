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
);

public sealed record MyCardDto(
    string LastFour,
    decimal CreditLimit,
    decimal AvailableCredit,
    decimal CurrentDebt,
    string Expiration
);
