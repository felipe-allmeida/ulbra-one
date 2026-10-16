using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Identity.Application.Contracts;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Identity.Domain;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Application.Endpoints.Auth.DevToken;

public sealed class DevToken : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("E-mail of the user to sign in as. Lower-cased before lookup. Omitted defaults to "
            + "dev@ulbra.br. If no user has this e-mail, one is created as Admin.")]
        [SwaggerExample("dev@ulbra.br")]
        [NormalizeText] public string? Email { get; set; }

        [SwaggerSchema("Display name used only when this call creates the user. Omitted defaults to "
            + "Dev User.")]
        [SwaggerExample("Dev User")]
        [NormalizeText] public string? Name { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/dev-token", Handle)
            .WithName("DevToken")
            .WithTags("Auth")
            .WithSummary("Issues a JWT without credentials. Development only.")
            .WithDescription("Local-development shortcut: returns a signed token for an e-mail, with no "
                + "password and no Google round-trip. Outside the Development environment it returns 404, "
                + "and the check is in the handler — the route exists in every environment's spec, so a 404 "
                + "here means the environment refused, not that the path is wrong. Creating a user as "
                + "Admin when the e-mail is unknown is deliberate for local setup, and is exactly why the "
                + "environment gate matters. Rate-limited like the real sign-in.")
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithRequestNormalizer<Request>()
            .Produces<LoginResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        Request req,
        IHostEnvironment env,
        IUnitOfWork uow,
        ITokenService jwt,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!env.IsDevelopment())
            return TypedResults.NotFound();

        var email = (req.Email ?? "dev@ulbra.br").ToLowerInvariant();
        var name = req.Name ?? "Dev User";

        var user = await uow.Set<User>().FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            user = new User(email, name, Roles.Admin);
            await uow.AddAsync(user, ct);
            user.RegisterLogin(clock.GetUtcNow());
            try
            {
                await uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Concurrent dev-token calls for the same email (e.g. parallel e2e
                // workers) race to create the user; the unique email index makes all
                // but one INSERT fail. Detach the rejected insert and load the row the
                // winning request created, then proceed with that user.
                foreach (var entry in ex.Entries)
                    entry.State = EntityState.Detached;
                user = await uow.Set<User>().FirstOrDefaultAsync(u => u.Email == email, ct);
                if (user is null)
                    throw;
                user.RegisterLogin(clock.GetUtcNow());
                await uow.SaveChangesAsync(ct);
            }
        }
        else
        {
            user.RegisterLogin(clock.GetUtcNow());
            await uow.SaveChangesAsync(ct);
        }

        // Inactive users are denied at login (AEL-883). Newly provisioned dev users default to Active.
        if (!user.Active)
            return ErrorResults.Forbidden("This user is inactive.", ErrorCodes.Auth.UserInactive);

        var permissions = await uow.Set<Role>()
            .Where(r => r.Name == user.Role)
            .SelectMany(r => r.Claims)
            .Select(c => c.Permission)
            .ToListAsync(ct);

        // Access scopes (companies) granted to the user, denormalized as claims in the token.
        var companyIds = await uow.Set<UserScopeGrant>()
            .Where(g => g.UserId == user.Id)
            .Select(g => g.CompanyId)
            .ToListAsync(ct);
        var scopes = companyIds.Select(ScopeClaim.Encode);

        // Finer per-unit / per-cost-center scopes (AEL-883): explicit intersection lists (no "empty = all"),
        // checked only for scoped roles (Requester/Approver) — see UserContext.CanAccessScope.
        var unitGrants = await uow.Set<UserUnitScopeGrant>()
            .Where(g => g.UserId == user.Id)
            .Select(g => new { g.CompanyId, g.UnitId })
            .ToListAsync(ct);
        var unitScopes = unitGrants.Select(g => UnitScopeClaim.Encode(g.CompanyId, g.UnitId));

        var costCenterGrants = await uow.Set<UserCostCenterScopeGrant>()
            .Where(g => g.UserId == user.Id)
            .Select(g => new { g.CompanyId, g.CostCenterId })
            .ToListAsync(ct);
        var costCenterScopes = costCenterGrants.Select(g => CostCenterScopeClaim.Encode(g.CompanyId, g.CostCenterId));

        var token = jwt.GenerateToken(user.Id.ToString(), user.Role, user.Name, user.Email, permissions, scopes, unitScopes, costCenterScopes);
        return TypedResults.Ok(new LoginResponse(token, new UserSummary(new UserId(user.Id), user.Email, user.Name, user.Role)));
    }
}
