# Registry Module
**Purpose:** Master data for the parties the ERP transacts with — vendors, customers, employees — plus CEP/CNPJ external lookups used to fill their data.
**Schema:** `registry`  ·  **UoW:** inject `IUnitOfWork` directly (no key — single non-keyed `AppDbContext`-backed UoW)
## Boundaries
- Belongs here: `VendorEntity`, `CustomerEntity`, `EmployeeEntity` (each carries the **`Entity` suffix**; response DTOs drop it → `VendorResponse`), the `Address` owned value object, and `PersonType`. Endpoints under `/vendors`, `/customers`, `/employees` (browse lists are **paginated**) plus `/lookup` (`LookupCep`, `LookupCnpj`).
- Vendor/Customer/Employee are **separate entities** — never merge them into one "party". Soft-delete + activate/deactivate are supported per entity.
- External lookups are typed `HttpClient`s: `ICepLookup → ViaCepLookup` (ViaCEP) and `ICnpjLookup → BrasilApiCnpjLookup` (BrasilAPI), registered in `RegistryModule`.
- Does NOT belong: org chart (Core), payables/finance (Finance), login users/auth (Identity). A vendor here is referenced by loose `VendorId` in Finance — no FK constraint crosses the registry↔finance boundary.
- Dependency rule: Domain → (nothing but Common.Domain); Application → Domain + Common.Application (NEVER Infrastructure); Infrastructure → Application + Domain.
## Key files
- `Ulbra.One.Registry.Domain/Entities/{VendorEntity,CustomerEntity,EmployeeEntity,PersonType}.cs`, `ValueObjects/Address.cs`
- `Ulbra.One.Registry.Application/Endpoints/{Vendors,Customers,Employees,Lookup}/…`
- `Ulbra.One.Registry.Infrastructure/Database/Configurations/{VendorConfiguration,CustomerConfiguration,EmployeeConfiguration}.cs` (each extends `BaseEntityConfiguration<T>` and declares `Schema => Schemas.Registry` **and** `TableName`, e.g. `"vendors"`; owned `Address`, partial unique index on document), `RegistryModule.cs` (`AddRegistryValidators`, lookup HttpClients)
## Testing
- Unit: `dotnet test tests/Ulbra.One.UnitTests` (validators, CPF/CNPJ check-digit via `DocumentValidator` — no Docker).
- Integration: `dotnet test tests/Ulbra.One.IntegrationTests` (Testcontainers Postgres + `ApiFactory.CreateAdminClientAsync()`).
- After mapping/entity change: `dotnet ef migrations add <Name> --project src/Ulbra.One.Persistence --startup-project src/API/Ulbra.One.Api --context AppDbContext` (never hand-write); commit the snapshot.
