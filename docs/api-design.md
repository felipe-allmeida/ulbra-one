# API Design — quick checklist

Backend API conventions, condensed. The long-form reference is
[`backend-conventions.md`](backend-conventions.md) — this is the "do X / don't Y" cheatsheet.
All examples below exist in the repo; when in doubt, the code wins.

## Vertical slice per operation

- **One folder per operation** under `…Application/Endpoints/<Resource>/<Operation>/` — e.g.
  `Endpoints/Payables/CreatePayable/CreatePayable.cs`, `Endpoints/Customers/CreateCustomer/`.
- Each file = one `public sealed class : IEndpoint` with `void Map(IEndpointRouteBuilder)` and a
  `private static async Task<IResult> Handle(...)`.
- The Request/Input DTO is **nested in the endpoint class** (Payable) or a shared Contract
  (`CustomerRequest`). Response DTO is a `sealed record` in `Application/Contracts/` with `From(entity)`.
- **Don't** add controllers. **Don't** put business logic in `Handle` — call the domain aggregate.

```csharp
public sealed class CreateCustomer : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("CreateCustomer").WithTags("Registry")
            .RequireAuthorization().WithRequestNormalizer<CustomerRequest>().WithValidation<CustomerRequest>()
            .Produces<CustomerResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);
}
```

## Route grouping

- The module's `MapXEndpoints()` (in the **Application** project, e.g. `RegistryEndpoints.cs`) creates
  one `builder.MapGroup("/vendors")` per resource and registers slices with `MapEndpoint<T>()`.
- Endpoint `Map` uses **relative** paths (`""`, `"/{id}"`, `"/{id}/payments"`) — the group owns the prefix.
- Resource paths are **kebab-case English** (`/cost-centers`, `/bank-accounts`, `/chart-accounts`).
- `.WithTags("Core"|"Finance"|"Registry"|"Auth")` per operation; `.WithName(...)` = stable operationId.

## Result / Problem Details

- Domain returns `Result` / `Result<T>`; map to HTTP with `.ToOk()` / `.ToCreated(loc)` (RFC 9457).
- **Every error goes through `ErrorResults`** (`Common.Application/Errors/`). Never build a problem by
  hand — the factories are the only thing that guarantees `trace_id` and `error_code` are present:

```csharp
if (cc is null) return ErrorResults.InvalidRequest(
    "Cost center is invalid for this company.", ErrorCodes.Payable.CostCenterInvalid);
```

- The factory names the situation, not the number: `InvalidRequest` (400), `UnprocessableEntity` (422),
  `InvalidState` (409), `NotFound`, `Forbidden`, `Unauthorized`, `TooManyRequests`, `ThirdPartyError`,
  `ServiceUnavailable`, `NotImplemented`, `InternalError`, and `ValidationError` for per-field failures.
- **Codes live in `ErrorCodes`** (`Common.Domain/Errors/`), never as literals at the call site:
  `payable.cost_center_invalid`, `vendor.not_found`. They are the API's public vocabulary — renaming one
  is a breaking change for the frontend.
- The envelope's two identity fields do different jobs and must not be conflated:
  - `title` — human summary, phrased by the factory (`"Invalid request"`). Handlers never write it.
  - `error_code` — the stable machine value the frontend keys its pt-BR message off.
  - `detail` — the specific English sentence for this occurrence; a developer/log-facing fallback.
  - `trace_id` — the current `Activity` trace id, so "it failed" can be tied to a request in telemetry.
- **Don't** localize on the backend — the frontend maps `error_code` → pt-BR (`errors.codes.*` in
  `web/src/i18n/locales/pt-BR.ts`). `ErrorCatalogueTests` fails the build when a code has no translation
  (or a translation has no code), so adding an error means adding both halves.
- **Always `TypedResults.*`, never `Results.*`** on the success path — typed results give compile-time
  response types and richer OpenAPI inference.
- `ErrorType → status` (railway): Validation→400, NotFound→404, Conflict→409, Unauthorized→401,
  Forbidden→403. `ResultExtensions` routes these through the same factories, so `.ToOk()` and an
  imperative guard produce byte-identical envelopes.

## Typed prefixed IDs

- IDs are `readonly record struct …Id(Guid Value) : IPrefixedId, IParsable<…>` in
  `Common.Application/Ids/`. Wire form is Stripe-style `prefix_{guid:N}` (e.g. `payable_3fa8…`).
- **Every typed ID used on the wire MUST be registered in `Program.cs` via `options.MapType<…Id>()`** —
  otherwise Swagger/openapi-typescript emits the wrong schema. New ID → add a `MapType` line.
- In handlers, unwrap with `req.CompanyId.Value`; build wire strings via `new PayableId(payable.Id)`.

## Normalization + validation (filters, not inline)

- Mark request string fields with `[NormalizeText]`; attach `.WithRequestNormalizer<TRequest>()` to trim/clean.
- Attach `.WithValidation<TRequest>()` → `ValidationEndpointFilter` resolves `IValidator<TRequest>` and
  returns a 400 grouped by field, through `ErrorResults` like every other error.
- Register validators per module (`AddValidatorsFromAssemblyContaining<…>`, e.g. `AddCoreValidators()`),
  and call it from `Program.cs`.

**Where each check goes.** The split is by what the check needs, not by taste:

| check | where | why |
|---|---|---|
| required, format, range, non-negative | **validator** | judged from the payload alone; belongs to every caller equally |
| entity exists, belongs to the company, is active | **handler** | needs the database, and the honest answer is 404 or 409, not a field error |
| uniqueness (a CNPJ already registered) | **handler** | needs the database; return 409, never let the unique index surface as a 500 |
| state machine ("a paid payable cannot be edited") | **domain** | it is an invariant of the aggregate, not of the request |

- **An aggregate with plain setters is not a validation layer.** `Company.Update` assigns `LegalName` and
  `Cnpj` without looking at them, which is why a company could be created with an empty razão social and a
  CNPJ of `"@@@"` and the API answered 201. If nothing above the setter checks, nothing checks.
- **Test fixtures must seed valid data.** Use `TestDocuments.RandomCnpj()` — a random 14-digit number is
  not a CNPJ, and seeding one only worked while nothing validated it.

## Pagination

- Browse lists (vendors, payables, transactions): take `[AsParameters] PaginationQueryParameters`
  (`page` 1-based, `pageSize` clamped `[1,100]`, default 20), order/filter, then `ToPaginatedAsync(p, ct)`.
- Returns `PaginatedResponse<T>(Items, Page, PageSize, Total, TotalPages)`; project with `.Map(Dto.From)`.
- Reference lists (companies, units, cost-centers): return a plain `List<TDto>` — no paging.

```csharp
var page = await query.OrderBy(v => v.Name).ToPaginatedAsync(pagination, ct);
return TypedResults.Ok(page.Map(VendorResponse.From));
```

## Persistence access (single UoW, no key)

- Inject `IUnitOfWork` directly — **no `[FromKeyedServices]`**, no module key. There is one
  non-keyed `IUnitOfWork` backed by the shared `AppDbContext` that covers every module's schema.
- A handler reads and writes across all schemas through the same `uow`:

```csharp
private static async Task<IResult> Handle(Request req,
    IUnitOfWork uow,
    TimeProvider clock, CancellationToken ct)
```

- Read via `uow.ReadSet<T>()`; write via `uow.AddAsync(entity, ct)` + `uow.SaveChangesAsync(ct)`.
- Cross-module reads (e.g. Payable loading a `Company` from core and a `VendorEntity` from registry)
  go through the same `uow` — one context, one connection, one transaction.
- Use the injected `TimeProvider` (`clock.GetUtcNow()`), **never `DateTime.UtcNow`**.
- **Audit** is the deliberate exception: it uses its own `AuditContext` registered under the keyed
  name `"audit"`. Do not key any other UoW.

## Response contract

- Map entity → response DTO (`record` + static `From`). **Don't** return entities/anonymous objects.
- Annotate every operation: `.Produces<TDto>(2xx)` + `.ProducesProblem(4xx)` so the generated TS client
  stays correct. DTO fields are emitted required; enums serialize as strings.
- **Response DTOs are documented too** — `[SwaggerSchema]` on the record (what the thing IS) and on each
  property (what the caller can't infer), `[SwaggerExample]` where the format misleads. It becomes JSDoc in
  `schema.d.ts`, so it is what a developer sees on hover. `///` XML comments do **not** reach the spec —
  there is no `IncludeXmlComments`; keep them for the C# editor and add the attribute alongside.
- **A domain type must never go straight onto the wire.** It can't carry `[SwaggerSchema]` (Domain doesn't
  reference Swashbuckle), so it is undocumentable by construction — wrap it in an Application DTO, the way
  `Address` → `AddressResponse` and `BankInfo` → `BankInfoResponse` already are.
- What a description is FOR is the non-obvious: that a figure is derived rather than stored, that two
  similar numbers overlap (`Consumed` = `Realized` + `Committed`) and must not be summed, that an empty
  collection means "no access" rather than "all access", that a field is stored but nothing reads it.
  Restating the field name adds nothing and still passes the guard — write it for the next developer.
- `ProblemDetails` is documented by `ProblemDetailsSchemaFilter` (it's a framework type), including the
  `trace_id` / `error_code` extension members that Swashbuckle can't see.

## Endpoint documentation

Every operation carries `.WithSummary()` + `.WithDescription()`, and every field a caller supplies carries
`[SwaggerSchema]` (what it means) and, where the format is guessable-wrong, `[SwaggerExample]` (one sample).
Reference implementation: the whole **Budgeting** module.

- **`.WithSummary()`** — one short sentence, imperative, ending in a period ("Approves a requisition for
  payment."). **`.WithDescription()`** — the rules a caller can't infer from the signature: what a 403/409
  actually means here, what is derived server-side, what is immutable, what an omitted filter implies.
- **A paginated list always gets a request object**, however few filters it has:
  `sealed record ListXRequest : PaginationQueryParameters`. `[AsParameters]` binds inherited properties but
  **does not nest**, so inheritance is what merges filters and paging into a single bound argument, and
  `ToPaginatedAsync(req, ct)` then takes the request itself.
  **Exception — a filter object shared with a non-paginated endpoint does NOT inherit it.** The paging
  properties would show up on that endpoint too, advertising a `page` it ignores. Keep the filters on their
  own and bind `[AsParameters] PaginationQueryParameters` separately on the paginated one
  (`ListPayablesRequest`, shared with the whole-set `payment-queue-summary`, is the case in the codebase).
- **Everything else wraps at 3+ query parameters**; below that, annotate the parameters inline — a whole
  type for one `?kind=` is noise.
- **Route ids need no description.** `PrefixedIdExampleParameterFilter` samples them from the registry, and
  the prefix already says which entity it is (`vendor_…`; `req_…` vs `reqatt_…` on the two-id attachment
  routes). Describe one only when the prefix genuinely leaves the meaning open.
- Descriptions are in **English**, like the rest of the backend. `page`/`pageSize` are documented once on
  `PaginationQueryParameters` — never redescribe them.
- Positional records annotate through the property: `record Request([property: SwaggerSchema("…")] string? Reason)`.
- **Don't** rely on XML `///` comments for the spec — there is no `IncludeXmlComments`, so Swashbuckle never
  reads them. Keep them for the IDE; the attributes are what ships. Same for `SwaggerSchema.Example`: the
  attribute has **no** Example property, which is why `[SwaggerExample]` exists.

```csharp
public sealed record ListRequisitionsRequest : PaginationQueryParameters
{
    [SwaggerSchema("Company that owns the requisitions. Omit for the consolidated view across every "
        + "company, which only a consolidated or scoped role may request.")]
    public CompanyId? CompanyId { get; set; }          // example comes from PrefixedIdRegistry

    [SwaggerSchema("Exact accounting competency (`YYYY-MM`) …")]
    [SwaggerExample("2026-08")]
    public string? Competency { get; set; }
}
```

Four filters in `Program.cs` (`EndpointDocumentationFilters.cs`) make the above come out right; they are
doc-only and change no runtime behavior. Each exists because of a verified OpenAPI 3.0 / Swashbuckle gap:

| Filter | Gap it closes |
|---|---|
| `ExampleSchemaFilter` | `[SwaggerSchema]` carries no Example — emits `[SwaggerExample]`, typed to the schema (`20` not `"20"`). |
| `RefPropertyDescriptionSchemaFilter` | An enum/object DTO property is a bare `$ref`, and 3.0 ignores a `$ref`'s siblings — wraps it in `allOf` so the description survives. Never write it on the shared component: it would leak onto every other field of that type. |
| `ParameterDescriptionFilter` | Same `$ref` problem for a **scalar enum query param** — lifts the description to the parameter, which sits outside the schema. |
| `PrefixedIdExampleParameterFilter` | `MapType`'s id example reaches body DTOs but never query params — re-reads it from the source-generated `PrefixedIdRegistry`, so a new `[PrefixedId]` is sampled the moment it is declared. |
| `CamelCaseQueryParameterFilter` | `[AsParameters]` names query params after the **properties** (`CompanyId`), while every caller sends `companyId`. Binding is case-insensitive, so this is the spec, not the wire. |

- New module Application project? Nothing to wire: `Swashbuckle.AspNetCore.Annotations` is referenced once in
  `Ulbra.One.Common.Application` and flows transitively (with the `Microsoft.OpenApi` pin that keeps NU1903
  from failing the build).

**This is enforced, not just documented.** `OpenApiContractTests` (integration suite) reads the generated
spec from the test host and fails the build when an operation has no summary or description, when a query
parameter or a request-DTO property has no description, or when `web/openapi.json` no longer matches the API.

**After changing anything a caller sends or any endpoint's shape, regenerate the frontend artifacts** —
there is no other guard on them, and they silently went a whole release out of date once:

```bash
dotnet run --project src/API/Ulbra.One.Api   # then save /swagger/v1/swagger.json over web/openapi.json
pnpm --dir web gen:api
```

The descriptions are not write-only: `openapi-typescript` emits them as JSDoc, so they show up on hover in
the editor when someone fills a request field on the frontend.
