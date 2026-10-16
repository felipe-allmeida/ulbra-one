---
name: regen-openapi
description: Use when regenerating the OpenAPI spec and the API types in this project (Ulbra.One) — usually one of the last steps of a PR, once the API contract changed. Covers both the endpoint documentation standard and the regen mechanics. Trigger when the user asks to "regenerate openapi", "regen da openapi", "update openapi.json", "regenerate the API types" or "run gen:api".
---

# Regenerate the OpenAPI (Ulbra.One)

Regenerating `web/openapi.json` + the frontend types is the final step once the API contract changed.
But before scraping Swagger, **confirm the endpoints you touched follow the repo's documentation standard**
(the #167–#179 campaign documented it module by module; the source of truth is `docs/api-design.md`
§"Endpoint documentation" + §"Response contract", and the **Budgeting module is the reference**). Regen only
captures what the endpoints already declare — a poorly-documented endpoint produces a poor TS client.

## 1. Before regenerating — endpoint documentation standard

For every new/changed endpoint (`…Application/Endpoints/<Resource>/<Operation>/<Operation>.cs`), check the
`Map(...)` chain and the request DTO:

- **`.WithName("Xyz")`** — a globally-unique, resource-qualified operationId (usually the class name; qualify it
  if it would collide, e.g. `ListMyRequisitionDecisions`). **`.WithTags(...)`** = the **resource** tag (one per
  `MapGroup`), not the module: `Finance`, `Budgeting`, `Accounting`, `Registry`, `Core`, `Fiscal`, `Auth`,
  `Users`, `Roles`, `Notifications`, `Audit` — there is **no** `Identity` tag.
- **`.WithSummary(...)`** — one short imperative sentence ending in a period ("Approves a requisition for payment.").
- **`.WithDescription(...)`** — the rules a caller can't infer from the signature, in English. Cover, when they
  apply: what each 4xx means *here*, what's derived server-side / immutable, what an omitted filter implies,
  **dev-only** endpoints (the route ships in every environment's spec and 404s outside Development), **LEGACY/inert**
  endpoints or fields (name the replacement), a **POST that doesn't mutate** (say why — e.g. calls a paid model),
  **collection setters that replace the whole set** (an omitted item is unlinked), and **soft-vs-hard delete** /
  idempotency.
- **Every response case documented** (the "case validation") — declare each status the handler actually returns,
  matching the `IResult` shape: `.Produces<TDto>(201)` for creates (incl. uploads), `.Produces<TDto>(200)` for
  gets/lists/transitions, `.Produces(204)` for deletes (bare, no type); **`.ProducesProblem(4xx)` for a Problem
  body, `.Produces(4xx)` (bare) for a bare `TypedResults.NotFound()`**. Vocabulary: 400 (validation), 403
  (permission — 401 only on endpoints that authenticate), 404, 409 (state-machine / uniqueness); Finance/CNAB also
  422; auth also 429/501. Same status with two causes → document both, split by Problem `title`
  (`Auth.DomainNotAllowed` vs `Auth.UserInactive`); a deliberate 404-instead-of-403 (anti-probing) goes in the
  description. Return `TypedResults.*`, never `Results.*` (richer OpenAPI inference).
- **Authorization:** `.RequirePermission(Permissions.X)` / `.RequireAuthorization()` / `.RequireAdmin()`
  (admin-only) — admin gating is usually a permission. Auth endpoints add `.AllowAnonymous()` +
  `.RequireRateLimiting("auth")`.
- **Validation → a 400 grouped by field**, in one of two sanctioned styles: the `.WithValidation<TRequest>()`
  filter + a FluentValidation `IValidator` (Registry), or a shared static `XValidation.Validate(...)` called at
  the top of `Handle` (Budgeting/reference). Either way, declare `.ProducesProblem(400)`. Normalize with
  `.WithRequestNormalizer<TRequest>()`, marking the request's string fields `[NormalizeText]` (the filter no-ops
  without the attribute). Uploads: `.DisableAntiforgery()` + bind form fields with `[FromForm]`.
- **Annotate every field on the wire** — sent *and* returned — with `[SwaggerSchema("what it means")]` — **including prefixed IDs and
  enums**. Add `[SwaggerExample("one sample")]` **only** where the format is guessable-wrong (strings/dates/
  numbers), and **withhold it from IDs and enums** (their schema/example already come out right). This applies to
  handler parameters too (`[FromForm]` fields, loose query params), not just DTO properties; positional records
  use `[property: SwaggerSchema("…")]`. **Route-path ids need no description** — a filter samples them and the
  prefix names the entity (`vendor_…`); describe one only when the prefix leaves the meaning open.
- **Query filters bind via `[AsParameters]`; wrap them into a request record at 3+ params, keep ≤2 inline.** A
  paginated list always gets a record (`sealed record ListXRequest : PaginationQueryParameters`) — **except** a
  filter shared with a non-paginated endpoint, which stays plain and binds `[AsParameters] PaginationQueryParameters`
  as a second parameter (so it can't advertise a `page` it ignores). `page`/`pageSize` are documented once on
  `PaginationQueryParameters` — never redescribe them. Reference lists (dropdowns) return a plain `List<TDto>` — no paging.

**Don't:** `.WithOpenApi(...)` / `IOpenApi*Transformer` (the project uses **Swashbuckle**, not built-in .NET
OpenAPI) · `///` XML for the spec (no `IncludeXmlComments`, so a `///` comment never reaches the spec — keep it for
the C# editor and put the doc in `[SwaggerSchema]`, on **response** DTOs as well as request ones) · `.ProducesValidationProblem()` / `.Accepts<T>()` ·
DataAnnotations (`[Required]`, `[Range]`, …) or `[FromQuery]`/`[FromRoute]`/`[FromBody]` · a manual `MapType` per
ID (the `PrefixedIdRegistry.All` loop in `Program.cs` registers them automatically). File-download endpoints
declare no `.Produces` (they return `TypedResults.File(...)`).

## 2. Regenerate the spec + the types

Run the API in **Development** (Swagger is served only there, on `:5001`), scrape it, and run gen:

```bash
dotnet run --project src/API/Ulbra.One.Api        # or: docker compose up -d — leave running; scrape from another shell, stop it after
curl.exe -s http://localhost:5001/swagger/v1/swagger.json -o web/openapi.json   # curl.exe, not PowerShell's curl alias (it mangles -s/-o); or run from the Bash shell
cd web && pnpm gen:api && pnpm checks              # gen:api = openapi-typescript openapi.json -o src/api/schema.d.ts
```

`pnpm checks` = `format:check + lint + typecheck`, but the meaningful gates for a regen are **lint +
typecheck** (+ `build`/`test` on a big diff) — `format:check` fails repo-wide here, so if it trips on files
you didn't touch, don't reformat the repo. If the API won't start (WDAC, port held by another session), bring
up an isolated instance: a throwaway postgres on a free port + native `dotnet run` with
`MassTransit__Host=disabled.invalid`, scrape that port, tear it down after.

## 3. Validate before committing

- **Documentation coverage** — check the *generated spec*, not just that it built: every operation has a summary
  + description, **0 query params without a description, 0 request-DTO properties without a description, 0
  PascalCase query params** (all camelCase). The campaign's whole-API baseline is 215 operations at full coverage.
- **The dump is real, not an error:** `web/openapi.json` must start with `{"openapi": "3.0.4"`. A 500 (e.g. a
  schemaId collision on nested `*Request`/`*Response` DTOs — fix in `Program.cs` `CustomSchemaIds`) gets saved
  as an error body by mistake.
- **YOUR change is in the spec:** `grep` for a symbol you added in `web/openapi.json` before trusting the
  dump. If it's missing, the usual cause is either the endpoint isn't registered (`IEndpoint` is **not**
  auto-discovered — add `MapEndpoint<T>()` in the module's `<Module>Endpoints.cs`, or it never reaches
  swagger) or `:5001` is another worktree's API (multi-session trap).
- **Expect a big diff:** the spec is not auto-synced, so regenerating pulls in all accumulated drift. `pnpm
  checks` (`tsc`) becomes the checker — fix whatever it flags; a big diff may also need `pnpm build`/`test`.
- **`web/src/api.ts` is a hand-written layer** over `schema.d.ts` (it corrects Swashbuckle quirks, e.g. a
  nullable enum emitted as required). Field optionality is fixed there, not via `gen:api`; new DTOs get a
  stable alias there. Drop hand-written stopgaps the regen made redundant.
- Commit on its own: `chore(<scope>): regenerate openapi.json + schema.d.ts`.

## Rules

- **Document before regenerating** — the `docs/api-design.md` standard is a prerequisite, not optional.
- **Never hand-edit `web/src/api/schema.d.ts`** (generated; Prettier/ESLint ignore it).
- **Don't use the Swashbuckle CLI** (`swagger tofile`) — it fails on this minimal-hosting app; scrape the
  live endpoint instead.
- Run against Development (`:5001`) and **verify the dump is yours**, not another session's.
