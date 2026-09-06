namespace ArtemisBankingPro.Domain.Common.Enums;

public enum ErrorCategory {
    Validation = 1,
    Conflict = 2,
    Declined = 3,
    NotFound = 4,
    Unauthorized = 5,
    Forbidden = 6,
    PreconditionFailed = 7,
}
