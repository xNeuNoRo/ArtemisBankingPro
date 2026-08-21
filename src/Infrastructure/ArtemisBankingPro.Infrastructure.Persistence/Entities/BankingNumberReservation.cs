namespace ArtemisBankingPro.Infrastructure.Persistence.Entities;

public enum BankingNumberResourceType {
    SavingsAccount = 1,
    Loan = 2,
}

public sealed class BankingNumberReservation {
    private BankingNumberReservation() { }

    public BankingNumberReservation(BankingNumberResourceType resourceType) {
        ResourceType = resourceType;
    }

    public BankingNumberReservation(string number, BankingNumberResourceType resourceType) {
        Number = number;
        ResourceType = resourceType;
    }

    public string Number { get; private set; } = null!;

    public BankingNumberResourceType ResourceType { get; private set; }
}
