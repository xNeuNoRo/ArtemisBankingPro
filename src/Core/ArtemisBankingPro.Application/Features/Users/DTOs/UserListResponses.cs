namespace ArtemisBankingPro.Application.Features.Users.DTOs;

/// <summary>
/// Public API item for the administrative user list.
/// </summary>
public sealed record UserListResponse(
    string Id,
    string UserName,
    string Identification,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    bool IsActive
);

/// <summary>
/// Public API item for the administrative commerce-user list.
/// </summary>
public sealed record CommerceUserListResponse(
    string Id,
    string UserName,
    string Identification,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    int CommerceId,
    string CommerceName,
    bool IsActive
);
