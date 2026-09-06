using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Features.Users.DTOs;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Users.Handlers;

/// <summary>
/// Crea un usuario con rol Comercio, lo asocia al comercio indicado (un único
/// usuario por comercio) y crea su cuenta de ahorro principal con el balance
/// inicial indicado. El usuario inicia inactivo y recibe el correo de activación.
/// </summary>
public sealed class CreateCommerceUserCommandHandler
    : IRequestHandler<CreateCommerceUserCommand, Result<CreateCommerceUserResponse>> {
    private readonly IUserAccountService _userAccountService;
    private readonly IUserRepository _userRepository;
    private readonly IMerchantRepository _merchantRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IAccountTokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly INumberGenerator _numberGenerator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CreateCommerceUserCommandHandler> _logger;

    public CreateCommerceUserCommandHandler(
        IUserAccountService userAccountService,
        IUserRepository userRepository,
        IMerchantRepository merchantRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        IAccountTokenService tokenService,
        IEmailService emailService,
        INumberGenerator numberGenerator,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        ILogger<CreateCommerceUserCommandHandler> logger
    ) {
        _userAccountService = userAccountService;
        _userRepository = userRepository;
        _merchantRepository = merchantRepository;
        _savingsAccountRepository = savingsAccountRepository;
        _financialOperationRepository = financialOperationRepository;
        _tokenService = tokenService;
        _emailService = emailService;
        _numberGenerator = numberGenerator;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async ValueTask<Result<CreateCommerceUserResponse>> Handle(
        CreateCommerceUserCommand message,
        CancellationToken cancellationToken
    ) {
        if (message.InitialAmount is null) {
            return Result.Failure<CreateCommerceUserResponse>(
                DomainError.Validation(
                    "User.InitialAmountRequired",
                    "El monto inicial es requerido."
                )
            );
        }

        if (await _merchantRepository.GetByIdAsync(message.CommerceId, cancellationToken) is null) {
            return Result.Failure<CreateCommerceUserResponse>(
                DomainError.NotFound(
                    "Commerce.NotFound",
                    "El comercio indicado no existe."
                )
            );
        }

        CreatedUserInfo? createdUser = null;
        string? mainAccountNumber = null;

        Result<string> transactionResult = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                // Releer dentro de la transacción evita que la comprobación de
                // asociación quede fuera de la carrera entre dos altas.
                Merchant? merchant = await _merchantRepository.GetByIdAsync(message.CommerceId, ct);
                if (merchant is null) {
                    return Result.Failure<string>(
                        DomainError.NotFound("Commerce.NotFound", "El comercio indicado no existe.")
                    );
                }

                if (merchant.AssociatedUserId is not null) {
                    return Result.Failure<string>(
                        DomainError.Conflict(
                            "Commerce.UserAlreadyAssociated",
                            "El comercio ya tiene un usuario asociado."
                        )
                    );
                }

                if (await _userRepository.ExistsByUserNameAsync(message.UserName, ct)) {
                    return Result.Failure<string>(
                        DomainError.Conflict(
                            "User.UserNameExists",
                            "Ya existe un usuario registrado con este nombre de usuario."
                        )
                    );
                }

                if (await _userRepository.ExistsByEmailAsync(message.Email, ct)) {
                    return Result.Failure<string>(
                        DomainError.Conflict(
                            "User.EmailExists",
                            "Ya existe un usuario registrado con este correo electrónico."
                        )
                    );
                }

                if (await _userRepository.ExistsByIdentityDocumentAsync(message.Identification, ct)) {
                    return Result.Failure<string>(
                        DomainError.Conflict(
                            "User.IdentificationExists",
                            "Ya existe un usuario registrado con esta cédula."
                        )
                    );
                }

                Result<CreatedUserInfo> createResult = await _userAccountService.CreateUserAsync(
                    message.FirstName,
                    message.LastName,
                    message.Identification,
                    message.Email,
                    message.UserName,
                    message.Password,
                    nameof(Roles.Comercio),
                    ct
                );
                if (createResult.IsFailure) {
                    return Result.Failure<string>(createResult.Error!);
                }

                createdUser = createResult.Value;
                Result<string> accountResult = await CreateCommerceAccountAsync(
                    merchant,
                    createdUser.UserId,
                    message.InitialAmount.Value,
                    ct
                );
                if (accountResult.IsFailure) {
                    return Result.Failure<string>(accountResult.Error!);
                }

                mainAccountNumber = accountResult.Value;
                return Result.Success(mainAccountNumber ?? string.Empty);
            },
            ct: cancellationToken
        );

        if (transactionResult.IsFailure) {
            return Result.Failure<CreateCommerceUserResponse>(transactionResult.Error!);
        }

        CreatedUserInfo user = createdUser
            ?? throw new InvalidOperationException("La creación terminó sin usuario Identity.");
        mainAccountNumber = string.IsNullOrEmpty(transactionResult.Value)
            ? null
            : transactionResult.Value;

        await SendActivationEmailAsync(user, message.CallbackUrl, CancellationToken.None);

        return Result.Success(
            new CreateCommerceUserResponse(
                user.UserId,
                user.UserName,
                user.Email,
                user.Role,
                user.IsActive,
                message.CommerceId
            )
        );
    }

    private async Task<Result<string>> CreateCommerceAccountAsync(
        Merchant merchant,
        string ownerUserId,
        decimal initialAmount,
        CancellationToken cancellationToken
    ) {
        var accountNumber = await _numberGenerator.NextAccountNumberAsync(cancellationToken);
        var numberResult = AccountNumber.Create(accountNumber);
        if (numberResult.IsFailure) {
            return Result.Failure<string>(numberResult.Error!);
        }

        var balanceResult = Money.Create(initialAmount);
        if (balanceResult.IsFailure) {
            return Result.Failure<string>(balanceResult.Error!);
        }

        var openedAt = _clock.Now;

        var openResult = SavingsAccount.OpenPrimary(
            ownerUserId,
            numberResult.Value,
            balanceResult.Value,
            _currentUser.UserId!,
            openedAt
        );
        if (openResult.IsFailure) {
            return Result.Failure<string>(openResult.Error!);
        }

        var account = openResult.Value;

        var associateResult = merchant.AssociateUser(ownerUserId, _clock.Now);
        if (associateResult.IsFailure) {
            return Result.Failure<string>(associateResult.Error!);
        }

        _merchantRepository.Update(merchant);
        await _savingsAccountRepository.AddAsync(account, cancellationToken);

        if (initialAmount > 0m) {
            var operationResult = FinancialOperation.Approve(
                Guid.NewGuid(),
                FinancialOperationKind.InitialFunding,
                balanceResult.Value,
                balanceResult.Value,
                Money.Zero,
                _currentUser.UserId!,
                openedAt,
                [
                    new AccountTransactionDetails(
                        numberResult.Value,
                        TransactionDirection.Credit,
                        balanceResult.Value,
                        "APERTURA_CUENTA_COMERCIO",
                        numberResult.Value.Value
                    ),
                ]
            );
            if (operationResult.IsFailure) {
                return Result.Failure<string>(operationResult.Error!);
            }

            await _financialOperationRepository.AddAsync(operationResult.Value, cancellationToken);
        }

        return Result.Success(numberResult.Value.Value);
    }

    private async Task SendActivationEmailAsync(
        CreatedUserInfo user,
        string? callbackUrl,
        CancellationToken cancellationToken
    ) {
        string rawToken = await _tokenService.GenerateAsync(
            user.UserId,
            AccountTokenType.Activation,
            cancellationToken
        );

        try {
            if (callbackUrl is null) {
                await _emailService.SendAsync(
                    user.Email,
                    new AccountActivationTokenModel(user.FullName, rawToken),
                    cancellationToken
                );
            }
            else {
                string activationLink =
                    $"{callbackUrl.TrimEnd('/')}/Auth/Activate?token={Uri.EscapeDataString(rawToken)}";
                await _emailService.SendAsync(
                    user.Email,
                    new AccountActivationModel(user.FullName, activationLink),
                    cancellationToken
                );
            }
        }
        catch (EmailSendException ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo de activación para el usuario {UserId}.",
                user.UserId
            );
        }
    }
}
