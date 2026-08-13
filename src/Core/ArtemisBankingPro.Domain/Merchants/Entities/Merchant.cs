using System.Net.Mail;
using ArtemisBankingPro.Domain.Common.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Merchants.Enums;
using ArtemisBankingPro.Domain.Merchants.Errors;
using ArtemisBankingPro.Domain.Merchants.Events;

namespace ArtemisBankingPro.Domain.Merchants.Entities;

/// <summary>
/// Representa un comerciante registrado en el sistema, con su información de contacto, RNC, estado y usuario asociado (si aplica).
/// </summary>
public sealed class Merchant : AggregateRoot<int> {
    private Merchant() { }

    private Merchant(
        string name,
        string? description,
        string email,
        string phoneNumber,
        string rnc,
        string createdByUserId,
        DateTimeOffset createdAt
    ) {
        Name = name;
        Description = description;
        Email = email;
        PhoneNumber = phoneNumber;
        Rnc = rnc;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
        Status = MerchantStatus.Active;
    }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public string Email { get; private set; } = null!;

    public string PhoneNumber { get; private set; } = null!;

    public string Rnc { get; private set; } = null!;

    public MerchantStatus Status { get; private set; }

    public string? AssociatedUserId { get; private set; }

    public string CreatedByUserId { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static Result<Merchant> Create(
        string name,
        string? description,
        string email,
        string phoneNumber,
        string rnc,
        string createdByUserId,
        DateTimeOffset createdAt
    ) {
        DomainError? validationError = Validate(name, email, phoneNumber, rnc, createdByUserId);
        if (validationError is not null) {
            return Result.Failure<Merchant>(validationError);
        }

        var merchant = new Merchant(
            name.Trim(),
            NormalizeOptional(description),
            email.Trim().ToLowerInvariant(),
            phoneNumber.Trim(),
            rnc.Trim(),
            createdByUserId,
            createdAt
        );

        merchant.RaiseDomainEvent(
            new MerchantCreatedEvent(merchant.Name, merchant.Rnc, merchant.CreatedAt)
        );

        return Result.Success(merchant);
    }

    public Result UpdateInformation(
        string name,
        string? description,
        string email,
        string phoneNumber,
        string rnc,
        DateTimeOffset updatedAt
    ) {
        DomainError? validationError = ValidateInformation(name, email, phoneNumber, rnc);
        if (validationError is not null) {
            return Result.Failure(validationError);
        }

        if (updatedAt < CreatedAt) {
            return Result.Failure(MerchantErrors.InvalidUpdateDate);
        }

        Name = name.Trim();
        Description = NormalizeOptional(description);
        Email = email.Trim().ToLowerInvariant();
        PhoneNumber = phoneNumber.Trim();
        Rnc = rnc.Trim();
        UpdatedAt = updatedAt;
        RaiseDomainEvent(new MerchantUpdatedEvent(Id, updatedAt));
        return Result.Success();
    }

    public Result AssociateUser(string userId, DateTimeOffset updatedAt) {
        if (AssociatedUserId is not null) {
            return Result.Failure(MerchantErrors.UserAlreadyAssociated);
        }

        if (string.IsNullOrWhiteSpace(userId)) {
            return Result.Failure(MerchantErrors.InvalidAssociatedUser);
        }

        if (updatedAt < CreatedAt) {
            return Result.Failure(MerchantErrors.InvalidUpdateDate);
        }

        AssociatedUserId = userId;
        UpdatedAt = updatedAt;
        RaiseDomainEvent(new MerchantUserAssociatedEvent(Id, userId));
        return Result.Success();
    }

    public Result Activate(DateTimeOffset updatedAt) {
        if (Status == MerchantStatus.Active) {
            return Result.Failure(MerchantErrors.AlreadyActive);
        }

        if (updatedAt < CreatedAt) {
            return Result.Failure(MerchantErrors.InvalidUpdateDate);
        }

        Status = MerchantStatus.Active;
        UpdatedAt = updatedAt;
        RaiseDomainEvent(new MerchantStatusChangedEvent(Id, true, updatedAt));
        return Result.Success();
    }

    public Result Deactivate(DateTimeOffset updatedAt) {
        if (Status == MerchantStatus.Inactive) {
            return Result.Failure(MerchantErrors.AlreadyInactive);
        }

        if (updatedAt < CreatedAt) {
            return Result.Failure(MerchantErrors.InvalidUpdateDate);
        }

        Status = MerchantStatus.Inactive;
        UpdatedAt = updatedAt;
        RaiseDomainEvent(new MerchantStatusChangedEvent(Id, false, updatedAt));
        return Result.Success();
    }

    private static DomainError? Validate(
        string name,
        string email,
        string phoneNumber,
        string rnc,
        string createdByUserId
    ) {
        DomainError? informationError = ValidateInformation(name, email, phoneNumber, rnc);
        if (informationError is not null) {
            return informationError;
        }

        return string.IsNullOrWhiteSpace(createdByUserId) ? MerchantErrors.InvalidCreator : null;
    }

    private static DomainError? ValidateInformation(
        string name,
        string email,
        string phoneNumber,
        string rnc
    ) {
        if (string.IsNullOrWhiteSpace(name)) {
            return MerchantErrors.InvalidName;
        }

        if (
            string.IsNullOrWhiteSpace(email)
            || !MailAddress.TryCreate(email.Trim(), out MailAddress? parsedEmail)
            || parsedEmail.Address != email.Trim()
        ) {
            return MerchantErrors.InvalidEmail;
        }

        if (string.IsNullOrWhiteSpace(phoneNumber)) {
            return MerchantErrors.InvalidPhoneNumber;
        }

        if (string.IsNullOrWhiteSpace(rnc)) {
            return MerchantErrors.InvalidRnc;
        }

        return null;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
