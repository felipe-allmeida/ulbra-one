# Identity Module
**Purpose:** Authentication and the application `User`. Validates Google sign-in, mints JWTs, and exposes the current identity. The only module with a public endpoint group.
**Schema:** `identity`  ·  **UoW:** inject `IUnitOfWork` directly (no key — single non-keyed `AppDbContext`-backed UoW)
## Boundaries
- Belongs here: `User` entity + `Roles`; auth endpoints under `/auth` — `GoogleSignIn` (`POST /auth/google`, validates Google ID token, enforces `AllowedDomains`, bootstraps `BootstrapAdmins`, upserts the user, returns a JWT), `DevToken` (`POST /auth/dev-token`, **Development-only**, 404 otherwise), and `Me` (current user). JWT minting (`JwtTokenService`) and `UserContext` live in/near this flow.
- `/auth` is the **only** group that is NOT `.RequireAuthorization()`; credential endpoints carry `.RequireRateLimiting("auth")` (rate limiting is effectively off in Development for tests/e2e).
- Read identity from `UserContext` (UserId/Role/IsAdmin/IsAuthenticated/**Permissions**/**HasPermission**), never from `HttpContext` directly. Admin gate is `.RequireAdmin()`; permission gate is `.RequirePermission(Permissions.X)`.
- Does NOT belong: org chart (Core), parties (Registry), finance (Finance). Authorization *policies* are consumed everywhere but **defined/owned** here.
## Autorização por claim (Cargos → Permissões)
- **Cargo** (`Role`) agrupa permissões (`RoleClaim`, formato `recurso:ação`) semeadas em `Database/Configurations/RoleSeed.cs`. Cargos (nomes em inglês): `Admin` (bypass global — sem claims), `Finance`, `Controllership`, `Accounting`, `User`. `User.Role` referencia o cargo por **nome**.
- **Catálogo** de permissões: `Common.Application/Authentication/Permissions.cs` (vocabulário compartilhado — módulos referenciam por constante). Endpoints exigem uma permissão com `.RequirePermission(Permissions.X)`; Admin faz bypass.
- **Emissão**: os endpoints de login carregam `Role.Claims` do cargo do usuário e as passam a `ITokenService.GenerateToken(...)`, que emite uma claim `permission` por permissão. `PermissionPolicyProvider` (registrado em `AddJwtAuthentication`) resolve as policies `perm:<permissão>` sob demanda.
- **Front**: `/auth/me` devolve `permissions: string[]`; o SPA (`web/src/permissions.ts`) usa `can()`/`canAny()` para renderizar o menu por cargo (`web/src/nav.config.tsx` → `requires`).
- Dependency rule: Domain → (nothing but Common.Domain); Application → Domain + Common.Application (NEVER Infrastructure); Infrastructure → Application + Domain.
## Key files
- `Ulbra.One.Identity.Domain/Entities/{User,Role,RoleClaim}.cs`, `Ulbra.One.Identity.Domain/Roles.cs`
- `Ulbra.One.Identity.Application/Endpoints/Auth/{GoogleSignIn,DevToken,Me}/…`
- `Ulbra.One.Identity.Infrastructure/Database/Configurations/{UserConfiguration,RoleConfiguration,RoleClaimConfiguration,RoleSeed}.cs`, `IdentityModule.cs` (`AddIdentityModule`, JWT auth wiring)
- Authorization (Common): `Common.Application/Authentication/{Permissions,PermissionPolicyProvider,PermissionAuthorizationExtensions}.cs`
## Testing
- Unit: `dotnet test tests/Ulbra.One.UnitTests` (JWT/role logic — no Docker).
- Integration: `dotnet test tests/Ulbra.One.IntegrationTests` — auth is the entry point for every suite; `ApiFactory.CreateAdminClientAsync()` calls `/auth/dev-token`.
- After mapping/entity change: `dotnet ef migrations add <Name> --project src/Ulbra.One.Persistence --startup-project src/API/Ulbra.One.Api --context AppDbContext` (never hand-write); commit the snapshot.
- ⚠️ **Pendente**: as entidades `Role`/`RoleClaim` (+ seed) exigem uma migration ainda **não gerada** (branch criada sem SDK .NET local). Rode em ambiente com .NET: `dotnet ef migrations add AddRolesAndClaims --project src/Ulbra.One.Persistence --startup-project src/API/Ulbra.One.Api --context AppDbContext` e commite os arquivos + snapshot antes de subir/deployar (o startup aplica migrations e os endpoints de login consultam `role`/`role_claim`).
