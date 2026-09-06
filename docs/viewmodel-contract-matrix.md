# Artemis Banking Pro: ViewModel and Contract Matrix

## Status

Accepted baseline for Application contract design. Shared presentation
contracts from the matrix are implemented in Phase 2, Auth contracts in Phase
3, administrative feature ViewModels/mappings in Phase 4, Client/Cashier feature
ViewModels/mappings in Phase 5, explicit mapping composition/validation in Phase
6, presentation validation in Phase 7, and mapping/contract tests in Phase 8.
Phase 9 architecture ratification is recorded in ADR-018 and this matrix.
Phase 10 Application closure is recorded in `docs/application-phase10-gate.md`.
MVC controllers and Razor Views remain a later presentation phase.

## Scope

Application owns contracts consumed by the WebApp and API. Ownership does not
mean that every consumer reuses the same type:

```text
Application
├── Features/{Module}/ViewModels  MVC and presentation contracts
├── Features/{Module}/DTOs        API and transport contracts
├── Features/{Module}/Commands    write use-case contracts
└── Features/{Module}/Queries     read use-case contracts
```

MVC ViewModels and API DTOs remain separate even when their fields overlap. A
future client adds a contract for its own transport needs under Application; it
does not inherit Razor-specific binding concerns or sensitive fields from an
MVC model.

This distinction is deliberate: Application owns the contract vocabulary for
the current WebApp/API architecture, while each consumer owns the shape and
validation boundary appropriate to its transport. A separately versioned
contracts project is deferred until an independently deployed client requires
one.

## Sources of Truth

| Source | Scope used |
|---|---|
| `ProyectoArtemisBanking.md` §§72-4101 | MVC screens, roles, forms, confirmations, financial outcomes |
| `ProyectoArtemisBanking.md` §§4102-6703 | API endpoints, DTO payloads, auth, ownership and Hermes Pay |
| `ProyectoArtemisBanking.md` §§6704-6811 | ViewModels, validation, mapping, CQRS, testing and security requirements |
| `AGENTS.md` §§5-8, 15-19 | layer ownership, contract boundaries, Mapster, MVC/API and testing rules |
| `docs/adr-011-mapster-reinstated.md` | approved mapper and explicit mapping policy |
| `docs/adr-018-application-viewmodel-contracts.md` | accepted Application ownership and MVC/API contract separation |
| `docs/adr-015-dual-login-web-cookie-vs-api-jwt.md` | separate MVC cookie and API JWT login contracts |
| `docs/adr-017-admin-client-eligibility-composition.md` | shared server-side eligibility query for product assignment |
| `.opencode/plans/application-implementation-checklist.md` | existing Application module and contract inventory |

## Contract Rules

### Ownership

- ViewModels live under `Application/Features/{Module}/ViewModels`.
- Shared presentation models live under `Application/Common/ViewModels`.
- API DTOs remain under `Application/Features/{Module}/DTOs`.
- Commands and queries remain the only write/read use-case entry contracts.
- Domain entities, EF entities, `HttpContext`, `ClaimsPrincipal`, MVC types and
  `IFormFile` do not cross into ViewModels or DTOs.

### Input and output

- Input ViewModels are mutable binding models with only user-editable fields.
- Output ViewModels are read models; server-owned values are never bindable
  command input.
- IDs used by selection controls are hints only. Handlers re-read and authorize
  every selected resource.
- Hidden fields never carry balances, debt, effective amounts, roles, ownership,
  timestamps, status, or calculated totals as trusted input.
- Confirmation ViewModels may carry a server-issued single-use confirmation
  token, but the handler revalidates actor, operation, fingerprint and state.

### Validation

- DataAnnotations provide MVC binding feedback and unobtrusive client-side
  validation for required fields, formats, ranges and confirmation fields.
- FluentValidation remains authoritative for Application commands and queries.
- Mutable invariants remain in handlers, Domain policies, database constraints
  and concurrency protection; they are not delegated to ViewModels.
- API requests use DTO contracts and Application validators, never MVC-only
  validation behavior.
- Presentation validation is bounded to shape and user feedback. It does not
  authorize resources, calculate financial values or replace FluentValidation.

### Mapping

- Target structure: `Features/{Module}/Mapping/{Module}MappingRegister.cs`.
- Mapping must be explicit and feature-owned.
- Mapster ignores server-owned destination members on input mappings.
- When a command requires server-owned constructor context that cannot be
  represented safely by a source model, a feature-owned manual mapping is
  preferred over inventing placeholder values.
- Complex financial projections and calculations remain manual when that is more
  auditable than a mapper expression.
- `Common/Mapping/MapsterConfig.cs` composes the feature registers; feature
  registers own mappings for their module. Complex nested page collections stay
  explicit/manual when Mapster interface materialization is unsafe.

### Sensitive data

- MVC and API responses expose card mask or last four digits only.
- CVC, CVC hash, PAN, JWT, activation token and reset token never appear in
  response ViewModels or DTOs.
- Hermes Pay receives sensitive card input only through its API request contract;
  no sensitive value is retained in a response model.
- Account and loan numbers remain strings to preserve leading zeroes.
- All money and calculated rates use `decimal`.

## Status Legend

| Marker | Meaning |
|---|---|
| `UC` | Command/query and handler already exist in Application |
| `DTO` | Existing Application DTO or projection identified |
| `VM` | ViewModel required by this matrix; implementation status follows the phase plan |
| `MAP` | Feature mapping register required; not created in Phase 1 |
| `TEST` | Mapping, validation, security or flow tests required |
| `N/A` | No MVC ViewModel; internal process or API-only transport |

Phase 2 implemented the shared contracts in `SH-06` through `SH-10`. Phase 3
implemented the Auth ViewModels and Auth mapping register in `SH-01` through
`SH-05`. Phase 4 implemented the administrative contracts and feature mapping
registers in `ADM-01` through `ADM-22` and `ADM-24`. Phase 5 implements Client
and Cashier ViewModels, feature mappings and financial form/result tests.

## Shared MVC Contracts

| ID | Spec | Role/access | Screen or state | ViewModel | Application contract | DTO/response | Mapping and tests |
|---|---:|---|---|---|---|---|---|
| SH-01 | §§72-126 | Public, MVC | Login | `Auth/LoginViewModel` | `WebAppLoginCommand` | `WebAppLoginResponse` | `AuthMappingRegister`; DataAnnotations, role rejection, cookie-safe response; Phase 3 implemented |
| SH-02 | §§149-194 | Public, MVC | Request password reset | `Auth/RequestPasswordResetViewModel` | `RequestPasswordResetCommand` | `Result<Unit>` | `AuthViewModelMappings`; explicit server context for roles/callback, no token output; Phase 3 implemented |
| SH-03 | §§207-244 | Public, MVC | Reset password | `Auth/ResetPasswordViewModel` | `ResetPasswordCommand` | `Result<Unit>` | `AuthMappingRegister`; input token binding, password confirmation, single-use tests; Phase 3 implemented |
| SH-04 | §§127-148 | Public, MVC | Activate account | `Auth/ActivateAccountViewModel` | `ActivateAccountCommand` | `Result<Unit>` | `AuthMappingRegister`; input token validity and reuse tests; Phase 3 implemented |
| SH-05 | §§245-300 | Authenticated | Access denied | `Auth/AccessDeniedViewModel` | No command; role-aware navigation | N/A | Presentation-only model; server-resolved home navigation key; Phase 3 implemented |
| SH-06 | §§248-250, 6760-6765 | Any | Error and Problem Details bridge | `Common/ViewModels/ErrorViewModel` | `IErrorResponseMapper` | `ProblemDetails` for API | `CommonMappingRegister`; no stack traces or sensitive values |
| SH-07 | §§440-588, 2047-2273, 3167-3253 | Authenticated | Shared layout and role navigation | `Common/ViewModels/BaseViewModel` | Current authenticated context | N/A | Context-only fields; no balances, debt or product state |
| SH-08 | §§603-609, 964-966, 1424-1426, 1748-1750 | Authorized | Paginated list shell | `Common/ViewModels/PaginationViewModel` | `PageRequest` and `PageResult<T>` | Existing paged DTOs | Boundary tests for page 1 and max page size 20 |
| SH-09 | §§633-642, 1062-1071, 1774-1783 | Authorized | Select and radio options | `Common/ViewModels/SelectOptionViewModel` | Feature query or fixed allowed values | Feature DTOs | Allowed-value tests; no client-controlled role/state values |
| SH-10 | §§800-819, 1674-1702, 1958-2018, 2447-2464, 2733-2762, 3295-3314 | Authorized | Generic confirmation | `Common/ViewModels/ConfirmationViewModel` | Confirmation token service plus original command | Operation-specific result | Nonce actor/fingerprint/replay tests; never trust displayed totals |

## Administrator MVC Matrix

All rows below require server-side `Administrador` authorization. Every list
uses page 1 and page size 20 by default, stable ordering, SQL-side filtering
and pagination. Every financial mutation re-reads state and commits before
notification.

| ID | Spec | Screen or flow | ViewModel contract | Existing Application contract | Existing DTO/response | Required tests |
|---|---:|---|---|---|---|---|
| ADM-01 | §§441-588 | Dashboard | `Admin/AdminDashboardViewModel` | `GetAdminDashboardQuery` (`UC`) | `AdminDashboardDto` (`DTO`) | `MAP`, indicator aggregate and zero-active-client tests |
| ADM-02 | §§589-642, 923-947 | User list, filter and pagination | `Users/UserListViewModel`, `Users/UserListItemViewModel` | `GetUsersPagedQuery` (`UC`) | `PageResult<UserListDto>` (`DTO`) | `MAP`, role exclusion, ordering, pagination |
| ADM-03 | §§643-771 | Create web user | `Users/CreateUserViewModel` | `CreateUserCommand` (`UC`) | `CreateUserResponse` (`DTO`) | `MAP`, DataAnnotations, role-dependent amount, duplicate conflict |
| ADM-04 | §§772-795 | Activation email outcome | `Common/OperationResultViewModel` | `CreateUserCommand` (`UC`) | `CreateUserResponse` (`DTO`) | Post-commit email failure and safe notification tests |
| ADM-05 | §§823-947 | Edit user | `Users/UpdateUserViewModel` | `UpdateUserCommand` (`UC`) | `Result<Unit>` | `MAP`, no role editing, optional password, additional amount atomicity |
| ADM-06 | §§796-822 | Activate/inactivate confirmation | `Users/ChangeUserStatusViewModel` plus `ConfirmationViewModel` | `ChangeUserStatusCommand` (`UC`) | `Result<Unit>` | Self-protection, stale state, replay and authorization |
| ADM-07 | §§1011-1049 | Eligible client selection for loan | `Admin/EligibleClientsViewModel`, `Admin/EligibleClientItemViewModel` | `GetEligibleClientsQuery(Product=Loan)` (`UC`) | `EligibleClientsResponse` (`DTO`) | `MAP`, active client, no active loan, debt server calculation |
| ADM-08 | §§1050-1157 | Loan configuration and risk warning | `Loans/CreateLoanViewModel`, `Loans/HighRiskLoanConfirmationViewModel` | `CreateLoanCommand` (`UC`) | `CreateLoanResponse` or conflict details | `MAP`, term/rate/amount rules, confirmation nonce, 409 semantics |
| ADM-09 | §§959-1010 | Loan list, filters and search | `Loans/LoanListViewModel`, `Loans/LoanListItemViewModel` | `GetLoansPagedQuery` (`UC`) | `PageResult<LoanListDto>` (`DTO`) | `MAP`, active-first ordering, status and identification filters |
| ADM-10 | §§1284-1309 | Loan detail and amortization | `Loans/LoanDetailViewModel`, `Loans/LoanInstallmentViewModel` | `GetLoanDetailQuery` (`UC`) | `LoanDetailDto` (`DTO`) | `MAP`, overdue/status display, ownership of read data |
| ADM-11 | §§1310-1406 | Update loan rate | `Loans/UpdateLoanRateViewModel` | `UpdateLoanRateCommand` (`UC`) | `Result<Unit>` | `MAP`, future-pending-only rule, email-after-commit |
| ADM-12 | §§1419-1466, 1703-1730 | Credit-card list and filters | `CreditCard/CreditCardListViewModel`, `CreditCard/CreditCardSummaryViewModel` | `GetCreditCardsPagedQuery` (`UC`) | `PageResult<CreditCardSummaryDto>` (`DTO`) | `MAP`, masked number, status and identification filters |
| ADM-13 | §§1467-1503 | Eligible client selection for card | `Admin/EligibleClientsViewModel`, `Admin/EligibleClientItemViewModel` | `GetEligibleClientsQuery(Product=CreditCard)` (`UC`) | `EligibleClientsResponse` (`DTO`) | `MAP`, active client and debt server calculation |
| ADM-14 | §§1504-1589 | Assign credit card | `CreditCard/AssignCreditCardViewModel` | `AssignCreditCardCommand` (`UC`) | `AssignCreditCardResponse` (`DTO`) | `MAP`, positive limit, no PAN/CVC output, notification failure |
| ADM-15 | §§1590-1614 | Card detail and consumptions | `CreditCard/CreditCardDetailViewModel`, `CreditCard/CardConsumptionViewModel` | `GetCreditCardDetailQuery` (`UC`) | `CreditCardDetailDto`, `CardConsumptionDto` (`DTO`) | `MAP`, approved/rejected and AVANCE display |
| ADM-16 | §§1615-1673 | Update credit limit | `CreditCard/UpdateCardLimitViewModel` | `UpdateCardLimitCommand` (`UC`) | `Result<Unit>` | `MAP`, limit >= debt, concurrency and email-after-commit |
| ADM-17 | §§1674-1702 | Cancel credit card | `CreditCard/CancelCreditCardViewModel` plus `ConfirmationViewModel` | `CancelCreditCardCommand` (`UC`) | `Result<Unit>` | `MAP`, zero-debt invariant, replay and historical retention |
| ADM-18 | §§1743-1794, 2019-2046 | Savings-account list and filters | `SavingsAccounts/SavingsAccountListViewModel`, `SavingsAccounts/SavingsAccountSummaryViewModel` | `GetSavingsAccountsPagedQuery` (`UC`) | `PageResult<SavingsAccountSummaryDto>` (`DTO`) | `MAP`, active/type filters, principal-first only where client-facing |
| ADM-19 | §§1795-1836 | Eligible client selection for secondary account | `Admin/EligibleClientsViewModel`, `Admin/EligibleClientItemViewModel` | `GetEligibleClientsQuery(Product=SecondarySavingsAccount)` (`UC`) | `EligibleClientsResponse` (`DTO`) | `MAP`, principal-account precondition |
| ADM-20 | §§1837-1899 | Assign secondary savings account | `SavingsAccounts/AssignSecondaryAccountViewModel` | `AssignSecondarySavingsAccountCommand` (`UC`) | `SavingsAccountResponse` (`DTO`) | `MAP`, non-negative initial balance, account-number collision |
| ADM-21 | §§1900-1957 | Account detail and transactions | `SavingsAccounts/AccountDetailViewModel`, `SavingsAccounts/AccountTransactionItemViewModel` | `GetAccountTransactionsQuery` (`UC`) | `AccountDetailDto`, `AccountTransactionDto` (`DTO`) | `MAP`, chronological ordering, debit/credit and approved/rejected |
| ADM-22 | §§1958-2018 | Cancel secondary account | `SavingsAccounts/CancelSecondaryAccountViewModel` plus `ConfirmationViewModel` | `CancelSecondarySavingsAccountCommand` (`UC`) | `Result<Unit>` | `MAP`, principal protection, paired balance transfer and atomicity |
| ADM-23 | §§589-601, 4573-4643 | Commerce user list for API administration | No MVC screen in functional document; future admin UI contract reserved | `GetCommerceUsersPagedQuery` (`UC`) | `PageResult<UserListDto>` (`DTO`) | API contract tests; no accidental inclusion in web-user list |
| ADM-24 | §§1549, 4481-4530 | Assign commerce user | `Merchants/AssignCommerceUserViewModel` | `CreateCommerceUserCommand` (`UC`) | `CreateCommerceUserResponse` (`DTO`) | `MAP`, server-resolved commerce, active association and no sensitive output |

## Client MVC Matrix

All rows below require authenticated `Cliente` authorization and ownership
enforcement. Select options are generated from server-owned active products.

| ID | Spec | Screen or flow | ViewModel contract | Existing Application contract | Existing DTO/response | Required tests |
|---|---:|---|---|---|---|---|
| CLI-01 | §§2047-2273 | Home and active products | `Client/ClientDashboardViewModel`, `Client/MyProductsViewModel` | `GetMyProductsQuery` (`UC`) | `MyProductsDto` (`DTO`) | `MAP`, ownership, active-only, empty product sections; Phase 5 implemented |
| CLI-02 | §§2115-2170 | Own savings-account detail | `Client/MyAccountTransactionsViewModel`, `Client/ClientAccountTransactionItemViewModel` | `GetMyAccountTransactionsQuery` (`UC`) | `PageResult<AccountTransactionDto>` (`DTO`) | `MAP`, ownership, pagination, no cross-client access; Phase 5 implemented |
| CLI-03 | §§2190-2209 | Own loan detail | `Client/MyLoanDetailViewModel`, `Client/MyLoanInstallmentViewModel` | `GetMyLoanDetailQuery` (`UC`) | `MyLoanDetailDto`, `AmortizationRowDto` (`DTO`) | `MAP`, ownership, delinquency and installment states; Phase 5 implemented |
| CLI-04 | §§2225-2251 | Own card detail | `Client/MyCardDetailViewModel`, `Client/MyCardConsumptionViewModel` | `GetMyCardDetailQuery` (`UC`) | `MyCardDetailDto`, `MyCardConsumptionDto` (`DTO`) | `MAP`, ownership, last four only, approved/rejected; Phase 5 implemented |
| CLI-05 | §§2274-2385 | Beneficiary list | `Client/BeneficiaryListViewModel`, `Client/BeneficiaryItemViewModel` | `GetMyBeneficiariesQuery` (`UC`) | `MyBeneficiaryDto` (`DTO`) | `MAP`, owner-only list, active destination; Phase 5 implemented |
| CLI-06 | §§2300-2352 | Add beneficiary | `Client/AddBeneficiaryViewModel` | `AddBeneficiaryCommand` (`UC`) | `Result<Unit>` | `MAP`, nine-digit string, no own/duplicate/cancelled account; Phase 5 implemented |
| CLI-07 | §§2353-2371 | Remove beneficiary confirmation | `Client/RemoveBeneficiaryViewModel` plus `ConfirmationViewModel` | `RemoveBeneficiaryCommand` (`UC`) | `Result<Unit>` | `MAP`, relation-only delete, nonce replay and ownership; Phase 5 implemented |
| CLI-08 | §§2400-2505 | Express transfer form and confirmation | `Client/ExpressTransactionViewModel` plus `ConfirmationViewModel` | `ProcessExpressTransactionCommand` (`UC`) | `Result<Unit>` | `MAP`, funds, same-account rejection, paired ledger records, idempotency; Phase 5 implemented |
| CLI-09 | §§2506-2586 | Card payment | `Client/ClientCardPaymentViewModel` plus `ConfirmationViewModel` | `ProcessClientCardPaymentCommand` (`UC`) | `Result<Unit>` | `MAP`, effective amount capped to debt, ownership, idempotency; Phase 5 implemented |
| CLI-10 | §§2587-2688 | Loan payment | `Client/ClientLoanPaymentViewModel` plus `ConfirmationViewModel` | `ProcessClientLoanPaymentCommand` (`UC`) | `Result<Unit>` | `MAP`, oldest-installment allocation, cap to debt, concurrency; Phase 5 implemented |
| CLI-11 | §§2689-2787 | Beneficiary transfer form and confirmation | `Client/BeneficiaryTransferViewModel` plus `ConfirmationViewModel` | `ProcessBeneficiaryTransferCommand` (`UC`) | `Result<Unit>` | `MAP`, beneficiary ownership, paired ledger records, idempotency; Phase 5 implemented |
| CLI-12 | §§2812-3001 | Cash-advance quote and execution | `Client/CashAdvanceQuoteViewModel`, `Client/CashAdvanceViewModel` plus `ConfirmationViewModel` | `GetCashAdvanceQuoteQuery`, `ProcessCashAdvanceCommand` (`UC`) | `CashAdvanceQuoteDto`, `Result<Unit>` | `MAP`, 6.25% calculation, available credit, card/account ownership; Phase 5 implemented |
| CLI-13 | §§3002-3166 | Own-account transfer form and confirmation | `Client/OwnAccountsTransferViewModel` plus `ConfirmationViewModel` | `ProcessOwnAccountsTransferCommand` (`UC`) | `Result<Unit>` | `MAP`, active owned accounts, deterministic paired update, idempotency; Phase 5 implemented |

## Cashier MVC Matrix

All rows below require authenticated `Cajero` authorization. Cashier operation
inputs identify resources, but never supply actor, balance, effective amount,
status, timestamp or calculated debt.

| ID | Spec | Screen or flow | ViewModel contract | Existing Application contract | Existing DTO/response | Required tests |
|---|---:|---|---|---|---|---|
| CSH-01 | §§3167-3253 | Home and daily indicators | `Cashier/CashierDashboardViewModel` | `GetCashierDashboardQuery` (`UC`) | `CashierDashboardDto` (`DTO`) | `MAP`, current cashier/date scope, payment classification; Phase 5 implemented |
| CSH-02 | §§3254-3371 | Deposit form and confirmation | `Cashier/DepositViewModel` plus `ConfirmationViewModel` | `ProcessDepositCommand` (`UC`) | `CashierOperationResponse` (`DTO`) | `MAP`, active account, positive amount, post-commit email; Phase 5 implemented |
| CSH-03 | §§3372-3501 | Withdrawal form and confirmation | `Cashier/WithdrawalViewModel` plus `ConfirmationViewModel` | `ProcessWithdrawalCommand` (`UC`) | `CashierOperationResponse` (`DTO`) | `MAP`, atomic balance guard, rejected history, concurrency; Phase 5 implemented |
| CSH-04 | §§3502-3686 | Card payment form and confirmation | `Cashier/CardPaymentViewModel` plus `ConfirmationViewModel` | `ProcessCardPaymentCommand` (`UC`) | `CashierOperationResponse` (`DTO`) | `MAP`, internal CardId, last four output, effective payment cap; Phase 5 implemented |
| CSH-05 | §§3687-3906 | Loan payment form and confirmation | `Cashier/LoanPaymentViewModel` plus `ConfirmationViewModel` | `ProcessLoanPaymentCommand` (`UC`) | `CashierOperationResponse` (`DTO`) | `MAP`, oldest-installment allocation, cap, rejected history; Phase 5 implemented |
| CSH-06 | §§3908-4101 | Third-party transfer form and confirmation | `Cashier/ThirdPartyTransferViewModel` plus `ConfirmationViewModel` | `ProcessThirdPartyTransferCommand` (`UC`) | `ProcessThirdPartyTransferResponse` (`DTO`) | `MAP`, paired debit/credit atomicity, stable account order, idempotency; Phase 5 implemented |
| CSH-07 | Existing Application query | Cashier operation history, if exposed by WebApp navigation | `Cashier/CashierOperationListViewModel`, `Cashier/CashierOperationItemViewModel` | `GetCashierOperationsQuery` (`UC`) | `PageResult<CashierOperationDto>` (`DTO`) | `MAP`, current cashier scope, page bounds, safe product identifiers; Phase 5 implemented |

## API and Future-Client Contract Matrix

These rows are not MVC screens. They establish the API contract already
required by the functional document and identify the Application command/query
that future API, mobile or other clients will consume. API DTOs must not be
replaced by MVC ViewModels.

| Area | Endpoint | Application contract | DTO/response contract | Auth and contract checks |
|---|---|---|---|---|
| Auth | `POST /account/login` | `LoginCommand` | `LoginResponse` | Public; JWT only for active API roles |
| Auth | `POST /account/confirm` | `ActivateAccountCommand` | No content | Public; single-use token |
| Auth | `POST /account/get-reset-token` | `RequestPasswordResetCommand` | No content | Public; API token delivered by email body |
| Auth | `POST /account/reset-password` | `ResetPasswordCommand` | No content | Public; user/token binding and one-use |
| Users | `GET /api/users` | `GetUsersPagedQuery` | `PageResult<UserListDto>` | `Administrador`; excludes `Comercio`; max page size 20 |
| Users | `GET /api/users/commerce` | `GetCommerceUsersPagedQuery` | `PageResult<UserListDto>` | `Administrador`; only `Comercio` |
| Users | `POST /api/users` | `CreateUserCommand` | `CreateUserResponse` | `Administrador`; role allow-list; inactive creation |
| Users | `POST /api/users/commerce/{commerceId}` | `CreateCommerceUserCommand` | `CreateCommerceUserResponse` | `Administrador`; ownership and one user per commerce |
| Users | `PUT /api/users/{id}` | `UpdateUserCommand` | No content | `Administrador`; no role mutation |
| Users | `PATCH /api/users/{id}/status` | `ChangeUserStatusCommand` | No content | `Administrador`; self-status protection |
| Users | `GET /api/users/{id}` | `GetUserByIdQuery` | `UserDetailResponse` | `Administrador`; no secret fields |
| Loans | `GET /api/loan` | `GetLoansPagedQuery` | `PageResult<LoanListDto>` | `Administrador`; status and identification filters |
| Loans | `POST /api/loan` | `CreateLoanCommand` | `CreateLoanResponse` | `Administrador`; 409 high-risk confirmation |
| Loans | `GET /api/loan/{id}` | `GetLoanDetailQuery` | `LoanDetailDto` | `Administrador`; amortization detail |
| Loans | `PATCH /api/loan/{id}/rate` | `UpdateLoanRateCommand` | No content | `Administrador`; future pending installments only |
| Cards | `GET /api/credit-card` | `GetCreditCardsPagedQuery` | `PageResult<CreditCardSummaryDto>` | `Administrador`; masked card only |
| Cards | `POST /api/credit-card` | `AssignCreditCardCommand` | `AssignCreditCardResponse` | `Administrador`; no CVC/PAN response |
| Cards | `GET /api/credit-card/{id}` | `GetCreditCardDetailQuery` | `CreditCardDetailDto` | `Administrador`; recent-first consumptions |
| Cards | `PATCH /api/credit-card/{id}/limit` | `UpdateCardLimitCommand` | No content | `Administrador`; new limit >= debt |
| Cards | `PATCH /api/credit-card/{id}/cancel` | `CancelCreditCardCommand` | No content | `Administrador`; zero-debt cancellation |
| Accounts | `GET /api/savings-account` | `GetSavingsAccountsPagedQuery` | `PageResult<SavingsAccountSummaryDto>` | `Administrador`; status/type filters |
| Accounts | `POST /api/savings-account` | `AssignSecondarySavingsAccountCommand` | `SavingsAccountResponse` | `Administrador`; secondary only |
| Accounts | `GET /api/savings-account/{accountNumber}/transactions` | `GetAccountTransactionsQuery` | `AccountDetailDto` | `Administrador`; string account number, max 20 |
| Accounts | `PATCH /api/savings-account/{accountNumber}/cancel` | `CancelSecondarySavingsAccountCommand` | No content | `Administrador`; paired transfer when balance > 0 |
| Merchants | `GET /api/commerce` | `GetMerchantsPagedQuery` | `GetMerchantsPagedResponseDto` | `Administrador`; active default, max 20 |
| Merchants | `GET /api/commerce/{id}` | `GetMerchantByIdQuery` | `MerchantDetailDto` | `Administrador`; associated user is safe summary only |
| Merchants | `POST /api/commerce` | `CreateMerchantCommand` | `CreateMerchantResponse` | `Administrador`; unique RNC/email |
| Merchants | `PUT /api/commerce/{id}` | `UpdateMerchantCommand` | No content | `Administrador`; status excluded from body |
| Merchants | `PATCH /api/commerce/{id}/status` | `ChangeMerchantStatusCommand` | No content | `Administrador`; cascade user inactivity rules |
| Hermes Pay | `GET /pay/get-transactions/{commerceId}` | `GetCommerceTransactionsQuery` | `GetCommerceTransactionsResponseDto` | `Administrador` uses route; `Comercio` uses JWT ownership |
| Hermes Pay | `POST /pay/process-payment/{commerceId}` | `ProcessHermesPayCommand` | No content on success; `ProcessHermesPayResponseDto` on rejection | JWT, idempotency, CVC verification, atomic card/commerce writes |

## Internal Processes Without ViewModels

| Feature | Contract | Reason |
|---|---|---|
| `FinancialProcessors` | Processor interfaces and `FinancialOperationOutcome` | Application internals used by financial commands; no direct UI boundary |
| `Overdue` | `ProcessOverdueLoansCommand`, `ProcessedOverdueLoansCommand` | Azure Function and scheduled application workflow; no human form |
| `Operations` | Domain-event audit consumer | Post-commit observability/audit path; no presentation contract |

## Phase 3 Auth Contract Notes

- `LoginViewModel`, `RequestPasswordResetViewModel`, `ResetPasswordViewModel`
  and `ActivateAccountViewModel` are mutable MVC binding models, not API DTOs.
- `UserId` and `Token` in `ResetPasswordViewModel`, and `Token` in
  `ActivateAccountViewModel`, are transport-only values from the corresponding
  Auth flow. They remain untrusted and are verified by the existing Application
  handlers; they are never logged or returned by response models.
- `AllowedRoles` and `CallbackUrl` are deliberately absent from
  `RequestPasswordResetViewModel`. `AuthViewModelMappings` receives them from
  server-side composition so a form cannot select an API/MVC role set or an
  arbitrary callback URL.
- `AccessDeniedViewModel.HomeNavigationKey` is a server-resolved navigation key,
  not a client-supplied return URL or redirect target.
- DataAnnotations cover MVC feedback. Existing FluentValidation validators remain
  authoritative for all commands and all non-MVC callers.

## Mapping Register Plan

The following registers are required by the implemented MVC mapping boundary. Auth
was implemented in Phase 3; all listed MVC feature registers are now implemented
and composed by Phase 6:

```text
Features/Auth/Mapping/AuthMappingRegister.cs (implemented Phase 3)
Features/Admin/Mapping/AdminMappingRegister.cs
Features/Users/Mapping/UsersMappingRegister.cs
Features/Loans/Mapping/LoansMappingRegister.cs
Features/CreditCard/Mapping/CreditCardMappingRegister.cs
Features/SavingsAccounts/Mapping/SavingsAccountsMappingRegister.cs
Features/Merchants/Mapping/MerchantsMappingRegister.cs
Features/Client/Mapping/ClientMappingRegister.cs
Features/Cashier/Mapping/CashierMappingRegister.cs
```

Rules for each register:

- Map input ViewModels to commands, never directly to Domain entities.
- Ignore route IDs, role, ownership, balance, debt, status, timestamps, audit
  fields, calculated amounts and generated identifiers. Safe selection keys are
  permitted only when the handler revalidates them.
- Map DTOs to output ViewModels only when the UI contract is semantically the
  same; otherwise use a manual projection.
- Do not map CVC, CVC hash, PAN, JWT or reset/activation tokens to outputs.
- Test every non-trivial register and every sensitive-field exclusion.

## Validation and Security Test Catalog

| Test ID | Obligation |
|---|---|
| VM-01 | Required, format, range and password confirmation attributes produce expected ModelState errors |
| VM-02 | Decimal fields reject invalid input without changing server state |
| VM-03 | Nine-digit account and loan numbers, sixteen-digit card input and three-digit CVC remain strings |
| VM-04 | ViewModel input cannot set role, actor, ownership, balance, debt, status, timestamp or effective amount |
| VM-05 | Output models never contain PAN, CVC, CVC hash, JWT or reset/activation token |
| MAP-01 | Mapster configuration resolves every registered ViewModel/command and DTO/ViewModel mapping |
| MAP-02 | Sensitive and server-owned members are ignored explicitly |
| MAP-03 | Complex financial projections remain deterministic and do not recalculate money in presentation |
| AUTH-01 | MVC role separation returns Login or Access Denied as specified |
| AUTH-02 | API returns 401 versus 403 consistently for authentication and role failures |
| OWN-01 | Client product and transaction reads cannot cross ownership boundaries |
| FIN-01 | Confirmation tokens are actor-bound, operation-bound, single-use and replay-safe |
| FIN-02 | Rejected operations do not alter balances, debts, limits, installments or product state |
| FIN-03 | Paired financial records commit atomically and preserve operation correlation |
| FIN-04 | Email failure after commit does not roll back financial state |
| API-01 | API DTO shapes, pagination defaults and maximum page size 20 match the specification |
| API-02 | Hermes Pay derives commerce ownership from JWT for `Comercio` and ignores route spoofing |
| API-03 | Hermes Pay duplicate requests with the same idempotency key do not apply twice |

Phase 7 validation coverage includes VM-01 through VM-05 for the shared,
administrative, client and cashier ViewModel contracts. Phase 8 adds explicit
input/output mapping, nested pagination, server-owned context, confirmation,
status and sensitive-field coverage across all implemented mapping registers.
Phase 9 ratifies Application ownership, MVC/API separation, dual validation,
minimal shared contracts and future-client evolution without changing runtime
contracts. MVC integration tests remain pending until controllers and Razor
Views exist.

## Findings From Phase 1

| Finding | Severity | Required action |
|---|---|---|
| No `ViewModels` directories currently exist in Application | Required | Create contracts only after this matrix is accepted as the implementation order |
| `MapsterConfig` composes feature registers and validates them with explicit mapping | Resolved | Keep explicit feature composition and fail-fast compilation; do not add global assembly scanning without a new ADR |
| `UserListDto` is under `Application.Interfaces.Persistence.Repositories` | Required | Move or replace with `Features/Users/DTOs/UserListDto.cs` before exposing it as a stable consumer contract |
| Several DTO names are response-oriented while others are `Dto`-oriented | Consider | Normalize naming during each feature mapping pass; do not rename existing API payloads without contract review |
| Commerce and Hermes Pay have API contracts but no MVC screen | Intentional | Keep API-only; do not create unused MVC ViewModels |
| `Overdue`, `Operations` and `FinancialProcessors` have no presentation boundary | Intentional | Keep them out of ViewModel work |
| Functional document says AutoMapper; approved project baseline uses Mapster | Resolved | ADR-011 records Mapster as approved equivalent and preserves mapping requirement |

## Phase 1 Exit Criteria

- Every MVC screen and confirmation flow in §§72-4101 has a matrix row.
- Every API endpoint in §§4250-6481 has a matrix row.
- Every row identifies role, Application use case, existing DTO/response and
  future ViewModel or explicitly marks API-only/internal.
- Financial flows identify confirmation, idempotency, ownership, rejection and
  post-commit notification obligations.
- Sensitive fields and server-owned fields have explicit negative mapping rules.
- No ViewModel is created merely because a Domain entity or DTO exists.
- Phase 2 can create contracts in a deterministic order without inventing
  missing business behavior.

## References and Documentation Verified

- Mapster dependency registration and `ServiceMapper`: Mapster documentation,
  `https://github.com/MapsterMapper/Mapster/wiki/Dependency-Injection`.
- Mapster `IRegister` and explicit configuration: Mapster documentation,
  `https://github.com/MapsterMapper/Mapster/wiki/Config-location`.
- Mapster ignored destination members: Mapster documentation,
  `https://github.com/MapsterMapper/Mapster/wiki/Ignoring-members`.
- ASP.NET Core MVC model binding and ModelState validation:
  `https://learn.microsoft.com/aspnet/core/mvc/models/validation`.
- ASP.NET Core antiforgery for future state-changing MVC actions:
  `https://learn.microsoft.com/aspnet/core/security/anti-request-forgery`.
- FluentValidation ASP.NET Core integration:
  `https://docs.fluentvalidation.net/en/latest/aspnet.html`.
