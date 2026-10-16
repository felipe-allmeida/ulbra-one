using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Identity.Application.Contracts;
using Ulbra.One.Identity.Domain;
using Ulbra.One.Identity.Domain.Entities;
using Ulbra.One.Identity.Application.Services;

namespace Ulbra.One.Identity.Application.Endpoints.Auth.GoogleSignIn;

public sealed class GoogleSignIn : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("The Google ID token (JWT) obtained by the browser sign-in flow. It is "
            + "verified server-side against Google — this endpoint never sees a password.")]
        string IdToken);

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/google", Handle)
            .WithName("GoogleSignIn")
            .WithTags("Auth")
            .WithSummary("Signs in with a Google ID token.")
            .WithDescription("Exchanges a verified Google ID token for this API's own JWT, which carries "
                + "the user's role, permissions and scope. Sign-in is invitation-only: the e-mail must have "
                + "been pre-registered by an admin (POST /api/users), which is what determines the role the "
                + "first login already carries; only a configured bootstrap admin provisions itself. "
                + "Failure modes worth handling apart: 401 when the Google token is invalid or its e-mail is "
                + "unverified; 403 when the e-mail is not registered, or the account exists but is "
                + "deactivated; 429 when the rate limit trips; and 501 when Google sign-in is not configured "
                + "in this environment. The error_code tells the two 403s apart — auth.user_not_registered "
                + "against auth.user_inactive.")
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .Produces<LoginResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status501NotImplemented);
    }

    private static async Task<IResult> Handle(
        Request req,
        IGoogleTokenValidator validator,
        IOptions<GoogleAuthOptions> options,
        IUnitOfWork uow,
        ITokenService jwt,
        TimeProvider clock,
        CancellationToken ct)
    {
        var opt = options.Value;
        if (string.IsNullOrWhiteSpace(opt.ClientId))
            return ErrorResults.NotImplemented("Google ClientId is not configured.", ErrorCodes.Auth.GoogleNotConfigured);

        GoogleTokenPayload payload;
        try
        {
            payload = await validator.ValidateAsync(req.IdToken, opt.ClientId, ct);
        }
        catch (Exception)
        {
            return ErrorResults.Unauthorized("Invalid Google token.", ErrorCodes.Auth.GoogleTokenInvalid);
        }

        if (!payload.EmailVerified)
            return ErrorResults.Unauthorized("Email not verified by Google.", ErrorCodes.Auth.EmailNotVerified);

        var email = payload.Email.ToLowerInvariant();

        var user = await uow.Set<User>().FirstOrDefaultAsync(u => u.Email == email, ct);
        var isBootstrapAdmin = opt.IsBootstrapAdmin(email);

        if (user is null)
        {
            // Sign-in is invitation-only: the user must have been pre-registered by an admin
            // (POST /api/users), which is what carries the role and scope into this first login. The only
            // account that provisions itself is a configured bootstrap admin — otherwise a fresh
            // environment would have nobody able to register anyone.
            if (!isBootstrapAdmin)
                return ErrorResults.Forbidden($"Email '{email}' is not registered.", ErrorCodes.Auth.UserNotRegistered);

            user = new User(email, payload.Name, Roles.Admin);
            await uow.AddAsync(user, ct);
        }
        else if (isBootstrapAdmin && user.Role != Roles.Admin)
        {
            user.SetRole(Roles.Admin);
        }

        // Inactive users are denied at login (AEL-883). Newly provisioned users default to Active.
        if (!user.Active)
            return ErrorResults.Forbidden("This user is inactive.", ErrorCodes.Auth.UserInactive);

        user.RegisterGoogleLogin(payload.Subject, payload.Name, clock.GetUtcNow());
        await uow.SaveChangesAsync(ct);

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
