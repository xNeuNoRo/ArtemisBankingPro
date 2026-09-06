# Artemis Banking Pro: ViewModel Contract Policy

## Status

Accepted and implemented through the Phase 10 Application closure gate;
architecture ratified in Phase 9.

## Ownership and Naming

- Shared presentation contracts live under `Application/Common/ViewModels`.
- Feature contracts live under `Application/Features/{Module}/ViewModels`.
- MVC input models use a `{Action}{Entity}ViewModel` name, for example
  `CreateLoanViewModel`.
- MVC read models use `{Entity}ViewModel`, `{Entity}ListViewModel`,
  `{Entity}DetailViewModel` or `{Entity}SummaryViewModel` according to their
  screen responsibility.
- Feature-specific read models use a feature-qualified name when the same
  concept exists in more than one consumer boundary, for example
  `ClientAccountTransactionItemViewModel` versus
  `SavingsAccounts/AccountTransactionItemViewModel`.
- API DTOs, commands and queries remain in their existing feature folders and
  are never renamed to look like MVC ViewModels.
- One ViewModel represents one screen or one form responsibility. A list,
  detail, form and operation result do not share a catch-all model.

## Binding Policy

- ViewModels are contracts, not authorization boundaries.
- GET models are server-generated output. Context properties in
  `BaseViewModel`, calculated totals, statuses, roles, ownership, balances,
  debt, limits, timestamps and actor identity are never trusted from a POST.
- MVC POST actions must map an explicit allowlist of editable properties to an
  Application command. They must not pass the posted ViewModel as the command.
- Selection values are hints. Application handlers re-read, authorize and
  validate every selected resource.
- `ConfirmationViewModel.ConfirmationToken` is the sole common bindable field
  in a confirmation contract. It is a server-issued, single-use nonce and must
  never be logged or treated as a financial value.
- Return URLs, arbitrary controller/action names and arbitrary role/state values
  are not shared ViewModel fields. They require a separate allowlist if a
  feature genuinely needs them.

## Validation Policy

- DataAnnotations provide MVC binding feedback, required-field messages,
  formats, basic lengths/ranges and password confirmation UX.
- FluentValidation on Application commands and queries is authoritative for
  every caller, including API, MVC and Functions.
- Mutable financial invariants remain in handlers, Domain policies, database
  constraints and concurrency protection.
- Validation attributes never authorize ownership, determine effective amounts,
  calculate debt, or approve a state transition.

## Data and Dependency Policy

- ViewModels may contain presentation-safe output such as masked card data,
  last four digits and server-calculated balances when the screen requires it.
- PAN, CVC, CVC digests, JWTs, activation/reset tokens, cryptographic material
  and exception details never appear in response ViewModels. Auth input models
  may carry the activation/reset token required by that same flow; the value is
  transport-only, untrusted, single-use and never logged.
- Account, loan and other fixed-width identifiers remain `string` to preserve
  leading zeroes.
- All monetary and rate values use `decimal`.
- ViewModels do not reference Domain entities, EF entities, `HttpContext`,
  `ClaimsPrincipal`, MVC types or `IFormFile`.
- No `ViewModelBuilder<T>` or shared notification/result hierarchy is introduced
  until an actual screen demonstrates repeated composition that justifies it.

## Phase 2 Shared Contracts

The initial shared set is intentionally limited to:

- `BaseViewModel`: presentation context only.
- `ErrorViewModel`: safe user-facing error state.
- `PaginationViewModel`: server-produced list metadata aligned with
  `PageRequest` and `PageResult<T>`.
- `SelectOptionViewModel`: text-preserving selection option.
- `ConfirmationViewModel`: confirmation copy and server-issued nonce.

Feature ViewModels and feature mappings are added by the matrix phase that owns
their consumer. Notifications and operation results remain deferred until an
actual screen demonstrates repeated composition that justifies them.

## Phase 3 Auth Contracts

- `LoginViewModel` maps to `WebAppLoginCommand` and never to API `LoginResponse`.
- `RequestPasswordResetViewModel` contains only `UserName`. Allowed roles and
  callback URL are server-owned command context supplied by the explicit
  `AuthViewModelMappings` method rather than an artificial mapping with empty
  role context.
- `ResetPasswordViewModel` carries the route/form `UserId` and `Token` only for
  the reset flow, plus the two password fields. The handler revalidates token
  ownership, expiry and single use.
- `ActivateAccountViewModel` carries the activation token only for the
  activation flow. The handler remains authoritative for ownership and reuse.
- `AccessDeniedViewModel` exposes a server-resolved navigation key instead of a
  return URL, preventing open-redirect behavior in the future MVC controller.

## Phase 4 Administrative Contracts

- Admin, Users, Loans, CreditCard, SavingsAccounts and Merchants own their
  screen/form ViewModels and mapping registers under their feature folders.
- `EligibleClientsViewModel` is shared by administrative selection screens, but
  its `Product` and selected resource are revalidated server-side before use.
- Financial forms accept requested values only. Customer/card/account/loan IDs
  and idempotency keys are route, selection or server context and must overwrite
  posted values in mapping composition.
- Read models may show server-calculated balances, debt and limits. Those
  properties are init-only output and are never accepted as command input.
- Card read models expose only masked number/last four. No PAN, CVC, digest,
  fingerprint, password, token or cryptographic field is present.
- Paged nested collections use explicit feature mapping helpers rather than
  relying on automatic read-only interface materialization.

## Phase 5 Client and Cashier Contracts

- Client and Cashier financial forms contain only requested amounts and resource
  selectors/identifiers. They never bind actor, ownership, balance, debt,
  effective amount, status, timestamp, operation ID or notification outcome.
- MVC confirmation flows pass the server-issued idempotency key to the mapping
  helper; it is not taken from a hidden form field or generated by the browser.
- Client product summaries expose additive internal `LoanId` and `CardId` values
  solely to navigate existing authorized commands. Ownership is re-read by the
  handler and is never inferred from the identifier.
- Cashier operation result models expose the persisted operation ID and safe
  post-commit notification warning because the existing cashier response DTOs
  already provide those fields. Client financial commands currently return
  `Result<Unit>`, so no unsupported client result model is fabricated.
- Card payment forms use the existing internal card ID command contract; they do
  not bind the 16-digit PAN described by the functional screen text.

## Phase 6 Mapping Composition

- Each implemented consumer feature owns one `IRegister` under its `Mapping`
  folder. `Common/Mapping/MapsterConfig` composes the known registers explicitly;
  it does not scan the whole Application assembly.
- `RequireExplicitMapping` is enabled and the complete configuration is compiled
  during `MapsterConfig.Create()`. DI cannot expose an unvalidated mapper.
- Direct ViewModel-to-command/query mappings use safe placeholders for route IDs,
  ownership, actor identity and idempotency context. Feature helper methods take
  trusted server context separately and overwrite those placeholders.
- A selected resource identifier may be mapped from a form only when it is a
  legitimate selection hint and the handler re-reads, authorizes and validates
  the resource. It is never treated as ownership evidence.
- Complex nested paged results use explicit manual projections. Mapping must not
  calculate or mutate financial values.

## Phase 7 Validation

- DataAnnotations on mutable ViewModels provide presentation-time required,
  format, length, range, password-confirmation and cross-field feedback.
- Text filters and route identifiers have bounded presentation input, while
  Application validators remain responsible for allowed values, pagination,
  authorization, ownership and mutable state.
- Financial ViewModels validate only requested shape and positive/non-negative
  input. They never validate or calculate balance, debt, available credit,
  effective payment, risk or other server-owned results.
- Fixed-width account identifiers remain strings with exact nine-digit
  presentation validation, preserving leading zeroes.
- `IValidatableObject` is used only for local presentation relationships such
  as date ordering, same-account rejection and role-dependent initial amounts.
  The corresponding FluentValidation rule remains authoritative for every
  non-MVC caller.
- `Validator.TryValidateObject` tests cover the shared contracts and feature
  form/filter contracts. MVC ModelState will remain the binding boundary when
  controllers are introduced.

## Phase 9 Architecture Ratification

The following rules are locked for the current application:

- Application is the ownership layer for ViewModels and transport contracts,
  despite the generic WebApp ownership wording in `AGENTS.md`; this is the
  explicit course/project exception recorded in ADR-018.
- MVC ViewModels remain separate from API DTOs, Commands and Queries. Sharing
  the Application project does not mean sharing the same type across consumers.
- DataAnnotations are presentation feedback. FluentValidation and the existing
  handler, Domain, database and concurrency boundaries remain authoritative.
- `BaseViewModel` remains minimal. No speculative `ViewModelBuilder<T>` or
  global result model is added without a concrete repeated screen composition
  requirement.
- Future mobile or external clients define separate feature contracts in
  Application. A separately versioned contracts project is deferred until an
  independently deployed client requires it.

The policy is intentionally compatible with mixed-version MVC/API deployment:
adding a new consumer contract does not alter existing API DTO shapes. Removing
or renaming an existing DTO requires the normal API compatibility review.

## References

- `docs/adr-018-application-viewmodel-contracts.md`
- `docs/viewmodel-contract-matrix.md`
- `ProyectoArtemisBanking.md` §§6704-6811
- `AGENTS.md` §§5-8, 15-19
- Mapster official documentation: `IRegister`, dependency injection and
  explicit configuration validation.
- FluentValidation official ASP.NET Core integration documentation.
- ASP.NET Core official model binding and validation documentation.
