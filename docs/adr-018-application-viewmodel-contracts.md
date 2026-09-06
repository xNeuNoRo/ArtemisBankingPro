# ADR 018: Application-Owned Consumer Contracts and ViewModel Matrix

## Status

Accepted

## Date

2026-08-17

## Context

The functional document requires ViewModels for the WebApp, validation at the
presentation boundary, and mapping between ViewModels, entities and DTOs. The
reference projects used by the course place ViewModels in Application. Artemis
already places commands, queries and DTOs in Application, but currently has no
formal ViewModel inventory or screen-to-use-case traceability.

The repository's general layer guidance assigns MVC ViewModels to WebApp. That
guidance conflicts with the explicit course pattern and the owner's decision to
keep Application contracts portable for the proposed WebApp and API clients.
The explicit project requirement and course convention take precedence for this
decision.

## Decision

Application owns consumer-facing contracts with a strict consumer boundary:

```text
Application/Features/{Module}/ViewModels  MVC/presentation contracts
Application/Features/{Module}/DTOs        API and transport contracts
Application/Features/{Module}/Commands    write use-case contracts
Application/Features/{Module}/Queries     read use-case contracts
```

We adopt the following rules:

1. ViewModels live in feature folders under Application, with only generic
   presentation primitives under `Application/Common/ViewModels`.
2. MVC ViewModels and API DTOs are separate types. Application is the shared
   ownership layer, not a reason to reuse a Razor binding model as an API
   contract.
3. Future mobile or external clients define their own DTO/request/response
   contracts in Application when the client is introduced. They do not inherit
   MVC-specific fields or validation behavior.
4. Input ViewModels use DataAnnotations for MVC binding feedback. FluentValidation
   on Application commands and queries remains authoritative for all callers.
5. Mapster is the approved mapper. New mappings are feature-owned, explicit and
   tested. Input mappings ignore server-owned and sensitive fields.
6. A generic `ViewModelBuilder<T>` is not introduced until repeated composition
   proves a concrete need. Shared context uses a small `BaseViewModel` only.
7. No ViewModel contains Domain entities, EF entities, HTTP context, MVC-specific
   file types, PAN, CVC, authentication/activation/reset tokens, balances or debt
   as trusted input. A confirmation ViewModel may carry only the server-issued
   single-use confirmation nonce defined by the confirmation service.

The complete Phase 1 inventory is maintained in
`docs/viewmodel-contract-matrix.md`.

Phase 2 implemented the shared presentation baseline in
`Application/Common/ViewModels`: `BaseViewModel`, `ErrorViewModel`,
`PaginationViewModel`, `SelectOptionViewModel` and `ConfirmationViewModel`.
Their exact binding, validation and sensitive-data rules are maintained in
`docs/viewmodel-contract-policy.md`.

## Phase 9 Decision Ratification

The owner confirmed the five planning decisions. This section is the canonical
handoff from the implementation plan to the accepted architecture.

### 1. Location

ViewModels remain in Application, organized by feature:

```text
src/Core/ArtemisBankingPro.Application/
├── Common/ViewModels/
└── Features/{Module}/ViewModels/
```

This is a deliberate project-level exception to the generic layer wording that
assigns ViewModels to WebApp in `AGENTS.md` §6. The exception is justified by the
explicit course requirement and the existing reference-project convention. It
does not allow MVC rendering, `HttpContext`, `ClaimsPrincipal`, Razor types or
WebApp dependencies into Application.

### 2. Consumer separation

Application owns the contract types, but consumers do not share models merely
because they live in the same project:

```text
MVC form ViewModel -> Application Command/Query -> Application Response/DTO
API request DTO    -> Application Command/Query -> API response DTO
```

MVC ViewModels are not API DTOs. API DTOs are not MVC binding models. A future
client gets a contract shaped for its transport and lifecycle instead of
inheriting Razor-specific fields, DataAnnotations or hidden-field assumptions.

### 3. Dual validation

DataAnnotations provide MVC binding feedback, local relationships, formats,
lengths, ranges and password-confirmation UX. FluentValidation on Application
commands and queries remains authoritative for API, MVC, Functions and any
future client. Ownership, authorization, mutable financial state, concurrency,
uniqueness and effective monetary values remain outside ViewModel validation.

### 4. Minimal shared base

`BaseViewModel` stays limited to presentation context. A generic
`ViewModelBuilder<T>`, global notification hierarchy or catch-all screen model is
not introduced until actual MVC screens demonstrate repeated composition that
justifies it. This keeps the shared contract surface small and prevents global
financial or authorization state from becoming accidental form input.

### 5. Future clients

"Scalable for future apps" means stable ownership and independently evolvable
contracts, not literal reuse of MVC ViewModels. Application remains the current
contract ownership layer for WebApp and API. When a mobile or other client is
actually introduced, its request/response DTOs belong under the relevant feature
and remain separate from MVC ViewModels. A separately versioned contracts
project is deferred until an independently deployed client demonstrates that
need.

These decisions are accepted for the current educational system. They do not
claim production banking certification, PCI-DSS compliance or an independent
security audit.

## Phase 3 Auth Implementation

The first feature contracts are implemented under
`Application/Features/Auth/ViewModels`:

- `LoginViewModel`
- `RequestPasswordResetViewModel`
- `ResetPasswordViewModel`
- `ActivateAccountViewModel`
- `AccessDeniedViewModel`

`AuthMappingRegister` registers explicit Mapster mappings for the login, reset
password and activation commands. `RequestPasswordResetCommand` has two
server-owned members, `AllowedRoles` and `CallbackUrl`; therefore
`AuthViewModelMappings.ToRequestPasswordResetCommand` uses an explicit manual
mapping, requires those values as server-supplied arguments and accepts only
`UserName` from the form. This avoids an artificial Mapster mapping that would
have to invent invalid empty role context.

Activation and reset tokens are a deliberate input-only exception to the
sensitive-data rule: the corresponding link/form must transport them, but the
values remain untrusted, are revalidated by the existing handlers, are never
logged, and never appear in response ViewModels or API responses. `AccessDenied`
uses a server-resolved navigation key rather than a client-controlled return
URL.

## Phase 4 Administrative Implementation

Administrative contracts are implemented under feature-owned `ViewModels` and
`Mapping` folders for Admin, Users, Loans, CreditCard, SavingsAccounts and
Merchants. The contracts cover dashboard indicators, paginated lists, details,
selection screens, creation/edit forms, status changes and confirmation states
required by the functional document.

Form ViewModels contain only editable fields. Resource identifiers and
idempotency keys are accepted by mapping helpers as route/selection/server
context and overwrite any posted value before a command is sent. Financial
outputs remain read-only ViewModels and expose only the masked/last-four card
representation; no PAN, CVC, credential, token or cryptographic field is
introduced.

Feature mapping registers are composed by `Common/Mapping/MapsterConfig`.
Mapster handles scalar and direct contract mappings. Nested paged collections
are mapped explicitly in feature helpers because automatic materialization of
read-only interface collections is not reliable with the current Mapster
runtime. This keeps the financial read shape auditable without changing API
DTOs or handlers.

Mutation response DTOs that only lead to Post/Redirect/Get are intentionally
not wrapped in speculative result ViewModels; they will receive a screen
contract when a concrete MVC consumer needs to render them.

## Phase 5 Client and Cashier Implementation

Client and Cashier contracts are implemented under their feature-owned
`ViewModels` and `Mapping` folders. They cover product dashboards, owned-product
details, beneficiaries, transfers, payments, cash advances, cashier indicators,
cashier operations and post-commit operation results.

Financial form ViewModels accept only editable identifiers and requested amounts.
They do not accept actor IDs, ownership, balances, debt, effective amounts,
status, timestamps, operation IDs or notification outcomes. Mapping helpers
receive the server-issued idempotency key separately and overwrite any command
context before dispatch.

The existing client product summary DTO received additive `LoanId` and `CardId`
properties. The identifiers are required to navigate from the product dashboard
to the existing detail/payment commands; they are not authorization evidence and
handlers continue to re-read ownership. No PAN, CVC, card fingerprint or secret
was added.

Client financial commands currently return `Result<Unit>`, so no speculative
client transaction-result contract was invented. Cashier response DTOs already
contain operation IDs and post-commit notification warnings, therefore dedicated
cashier result ViewModels are justified and mapped for MVC presentation.

## Phase 6 Mapping Composition and Validation

The feature mapping registers are now the only composition units for ViewModel
and DTO mappings. `MapsterConfig` explicitly registers Auth, Admin, Users, Loans,
CreditCard, SavingsAccounts, Merchants, Client and Cashier registers. It enables
`RequireExplicitMapping` and compiles the complete `TypeAdapterConfig` before
registering it in DI, making incomplete mappings a startup failure.

Mappings that receive route-owned identifiers, actor/ownership context or
idempotency values use safe placeholders in the direct Mapster configuration.
Feature mapping helpers receive the trusted server context separately and
overwrite those values before command/query dispatch. Selection identifiers are
accepted only as hints and remain subject to handler authorization and state
checks. Complex paged financial projections remain manual and do not calculate
money in presentation mappings.

## Phase 8 Mapping Contract Testing

The mapping boundary is covered by a dedicated unit suite in
`ArtemisBankingPro.UnitTests/Application/Common/Mapping`. The suite verifies
input mappings across administrative, financial, Client and Cashier flows;
nested and paginated response mappings; server-owned context overrides;
confirmation/high-risk context; status and notification preservation; leading
zeroes; and card output exclusion of PAN/CVC/credential fields.

Phase 8 tests validate the contract boundary without moving authorization,
ownership, financial calculation or concurrency rules into ViewModels or
presentation mappings.

## Alternatives Considered

### Keep all ViewModels in WebApp

Rejected for this project. It conflicts with the functional/course pattern and
would make the intended Application contract inventory unavailable to the
proposed clients. It also leaves the mapping requirement under-specified.

### Reuse MVC ViewModels directly as API DTOs

Rejected. MVC binding, DataAnnotations, screen composition and browser concerns
would leak into the API. This would also increase the chance of exposing
server-owned financial fields or sensitive card data.

### Create a separate contracts project now

Deferred. There is no second independently versioned client requiring a separate
package yet. Application already owns the approved command/query/DTO boundary.
Extracting a project now would add deployment and dependency surface without a
demonstrated need.

### Introduce a generic ViewModel builder and base hierarchy immediately

Rejected. It would create speculative abstraction before any MVC screen exists.
The matrix reserves shared contracts and allows a builder later if composition
repeats across actual screens.

## Consequences

### Positive

- Course-required Application ViewModels are explicitly supported.
- API, MVC and future-client contracts remain independently evolvable.
- Screen-to-use-case traceability is established before implementation.
- Financial and security-sensitive fields have documented negative rules.
- Mapping, validation and test obligations are visible per feature.
- Future controllers remain thin because commands and queries are already named.

### Negative

- Some fields will exist in more than one contract when consumers have different
  semantics.
- Application becomes the owner of presentation contract types, increasing the
  need for strict dependency discipline.
- Existing DTO naming and `UserListDto` placement need normalization.
- Feature-owned Mapster registers add files, but make mapping ownership explicit.

## Migration and Rollback

Phase 1 was documentation-only and had no runtime migration. Phase 2 adds only
in-process Application contract types and unit tests; it does not change API
payloads, database schema, authentication behavior or financial workflows.

Implementation order:

1. Normalize contract placement where the current namespace violates the matrix.
2. Add shared ViewModels and Auth ViewModels.
3. Add feature ViewModels and mappings in the matrix order.
4. Add MVC controllers and views only after their Application contracts pass unit
   and mapping tests.
5. Keep existing API DTO shapes unchanged except for explicitly documented
   additive identifiers required to navigate existing client resource commands.
6. Retain old mappings until replacement mappings and tests pass; remove obsolete
   mappings only after all consumers are migrated.

Rollback during implementation means deleting or reverting only the new
ViewModel/mapping consumer while preserving existing commands, queries, DTOs and
API payloads. No database migration is required for this decision.

## Verification

- Matrix checked against `ProyectoArtemisBanking.md` MVC sections §§72-4101.
- Matrix checked against API endpoints §§4250-6481.
- Existing Application features and commands/queries inspected.
- Existing Mapster registration and ADR-011 inspected.
- Mapster dependency injection, `IRegister` and ignore-member guidance checked
  against current documentation.
- ASP.NET Core MVC ModelState/DataAnnotations guidance checked against current
  documentation.
- FluentValidation ASP.NET Core integration guidance checked against current
  documentation.
- Shared ViewModel contracts and their unit tests were added in Phase 2.
- Auth ViewModels, mappings and their validation/security tests were added in
  Phase 3 without changing API payloads, database schema, token generation,
  authentication handlers or financial runtime behavior.
- Administrative ViewModels, feature mapping registers, Mapster compilation
  coverage and form/server-owned-field tests were added in Phase 4 without
  changing API payloads, database schema, authorization handlers or financial
  workflows.
- Client and Cashier ViewModels, feature mapping registers, additive product
  identifiers, Mapster compilation coverage and financial form/security tests
  were added in Phase 5 without changing database schema, authorization handlers
  or financial write workflows.
- Phase 6 enabled explicit Mapster mapping, startup compilation, safe route/context
  mapping helpers and regression tests proving posted route identifiers do not
  populate server-owned command/query fields.
- Phase 7 added bounded presentation validation to feature filters and route
  action models, plus broad unit coverage for form shape, financial input,
  account formats, date relationships and sensitive contract boundaries. No
  API payload, financial handler, database schema or authorization rule changed.
- Phase 8 added the dedicated ViewModel mapping contract suite and verified the
  sensitive/server-owned mapping boundaries across all implemented registers.
- Phase 9 ratified the five architecture decisions above and synchronized the
  ADR, contract policy, matrix and implementation checklist. This phase changes
  documentation only; it does not change API payloads, database schema,
  authentication, authorization or financial runtime behavior.
- Phase 10 closed the Application contract gate. It added architecture-boundary
  tests, qualified the Client account transaction item name to avoid a cross-
  feature collision, and recorded the remaining MVC presentation work without
  changing API payloads, database schema, authentication, authorization or
  financial runtime behavior.

## References

- `docs/viewmodel-contract-matrix.md`
- `docs/application-phase10-gate.md`
- `docs/viewmodel-contract-policy.md`
- `ProyectoArtemisBanking.md`
- `AGENTS.md` §§5-8, 15-19
- `docs/adr-011-mapster-reinstated.md`
- `docs/adr-015-dual-login-web-cookie-vs-api-jwt.md`
- `docs/adr-017-admin-client-eligibility-composition.md`
- `.opencode/plans/application-implementation-checklist.md`

## Phase 9 References

- Mapster `IRegister`, dependency injection and explicit configuration:
  [Mapster configuration](https://github.com/MapsterMapper/Mapster/wiki/Config-location),
  [Mapster dependency injection](https://github.com/MapsterMapper/Mapster/wiki/Dependency-Injection),
  [Mapster configuration validation](https://github.com/MapsterMapper/Mapster/wiki/Config-validation-&-compilation)
- FluentValidation ASP.NET Core integration and validator registration:
  [FluentValidation ASP.NET Core](https://docs.fluentvalidation.net/en/latest/aspnet.html)
- ASP.NET Core model binding and validation:
  [Model validation](https://learn.microsoft.com/aspnet/core/mvc/models/validation)
  and [model binding](https://learn.microsoft.com/aspnet/core/mvc/models/model-binding)
