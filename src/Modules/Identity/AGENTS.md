# Identity Module
**Purpose:** Authentication and the application `User`. Validates Google sign-in, mints JWTs, and exposes the current identity. The only module with a public endpoint group.
**Schema:** `identity`  ·  **UoW key:** "identity"
## Boundaries
- Belongs here: `User` entity + `Roles`; auth endpoints under `/auth` — `GoogleSignIn` (`POST /auth/google`, validates Google ID token, enforces `AllowedDomains`, bootstraps `BootstrapAdmins`, upserts the user, returns a JWT), `DevToken` (`POST /auth/dev-token`, **Development-only**, 404 otherwise), and `Me` (current user). JWT minting (`JwtTokenService`) and `UserContext` live in/near this flow.
- `/auth` is the **only** group that is NOT `.RequireAuthorization()`; credential endpoints carry `.RequireRateLimiting("auth")` (rate limiting is effectively off in Development for tests/e2e).
- Read identity from `UserContext` (UserId/Role/IsAdmin/IsAuthenticated), never from `HttpContext` directly. Admin gate is `.RequireAdmin()`.
- Does NOT belong: org chart (Core), parties (Registry), finance (Finance). Authorization *policies* are consumed everywhere but **defined/owned** here.
- Dependency rule: Domain → (nothing but Common.Domain); Application → Domain + Common.Application (NEVER Infrastructure); Infrastructure → Application + Domain.
## Key files
- `Ulbra.One.Identity.Domain/Entities/User.cs`, `Ulbra.One.Identity.Domain/Roles.cs`
- `Ulbra.One.Identity.Application/Endpoints/Auth/{GoogleSignIn,DevToken,Me}/…`
- `Ulbra.One.Identity.Infrastructure/Database/IdentityContext.cs` (`Schema = "identity"`), `IdentityModule.cs` (`AddIdentityModule`, JWT auth wiring, UoW key `"identity"`)
## Testing
- Unit: `dotnet test tests/Ulbra.One.UnitTests` (JWT/role logic — no Docker).
- Integration: `dotnet test tests/Ulbra.One.IntegrationTests` — auth is the entry point for every suite; `ApiFactory.CreateAdminClientAsync()` calls `/auth/dev-token`.
- After mapping/entity change: `dotnet ef migrations add <Name> --project src/Modules/Identity/Ulbra.One.Identity.Infrastructure` (never hand-write); commit the snapshot.
