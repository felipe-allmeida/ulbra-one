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

- Domain returns `Result` / `Result<T>`; map to HTTP with `.ToOk()` / `.ToCreated(loc)` (RFC 7807).
- **Error code goes in Problem `title`** as a stable `Aggregate.Reason` string in English —
  `Payable.CostCenterInvalid`, `Vendor.NotFound`, `Transaction.AmountInvalid`. `detail` = English message.
- Simple guard clauses can use raw `Results.Problem(...)` with an explicit `title:` code:

```csharp
if (cc is null) return Results.Problem(detail: "Cost center is invalid for this company.",
    statusCode: 400, title: "Payable.CostCenterInvalid");
```

- **Don't** localize on the backend — the frontend maps `title` → pt-BR (`errors.codes.*`).
- `ErrorType → status`: Validation→400, NotFound→404, Conflict→409, Unauthorized→401, Forbidden→403.

## Typed prefixed IDs

- IDs are `readonly record struct …Id(Guid Value) : IPrefixedId, IParsable<…>` in
  `Common.Application/Ids/`. Wire form is Stripe-style `prefix_{guid:N}` (e.g. `payable_3fa8…`).
- **Every typed ID used on the wire MUST be registered in `Program.cs` via `options.MapType<…Id>()`** —
  otherwise Swagger/openapi-typescript emits the wrong schema. New ID → add a `MapType` line.
- In handlers, unwrap with `req.CompanyId.Value`; build wire strings via `new PayableId(payable.Id)`.

## Normalization + validation (filters, not inline)

- Mark request string fields with `[NormalizeText]`; attach `.WithRequestNormalizer<TRequest>()` to trim/clean.
- Attach `.WithValidation<TRequest>()` → `ValidationEndpointFilter` resolves `IValidator<TRequest>` and
  returns a 400 `ValidationProblem` grouped by field. **Don't** validate inside `Handle`.
- Register validators per module (`AddValidatorsFromAssemblyContaining<…>`, e.g. `AddRegistryValidators()`).

## Pagination

- Browse lists (vendors, payables, transactions): take `[AsParameters] PaginationQueryParameters`
  (`page` 1-based, `pageSize` clamped `[1,100]`, default 20), order/filter, then `ToPaginatedAsync(p, ct)`.
- Returns `PaginatedResponse<T>(Items, Page, PageSize, Total, TotalPages)`; project with `.Map(Dto.From)`.
- Reference lists (companies, units, cost-centers): return a plain `List<TDto>` — no paging.

```csharp
var page = await query.OrderBy(v => v.Name).ToPaginatedAsync(pagination, ct);
return Results.Ok(page.Map(VendorResponse.From));
```

## Persistence access (keyed UoW per module)

- Inject the unit of work **by module key**: `[FromKeyedServices("finance")] IUnitOfWork uow`.
- A handler may read across modules by key (Payable reads `core` + `registry`):

```csharp
private static async Task<IResult> Handle(Request req,
    [FromKeyedServices("finance")] IUnitOfWork uow,
    [FromKeyedServices("core")] IUnitOfWork core,
    [FromKeyedServices("registry")] IUnitOfWork registry, TimeProvider clock, CancellationToken ct)
```

- Read via `uow.ReadSet<T>()`; write via `uow.AddAsync(entity, ct)` + `uow.SaveChangesAsync(ct)`.
- Use the injected `TimeProvider` (`clock.GetUtcNow()`), **never `DateTime.UtcNow`**.

## Response contract

- Map entity → response DTO (`record` + static `From`). **Don't** return entities/anonymous objects.
- Annotate every operation: `.Produces<TDto>(2xx)` + `.ProducesProblem(4xx)` so the generated TS client
  stays correct. DTO fields are emitted required; enums serialize as strings.
