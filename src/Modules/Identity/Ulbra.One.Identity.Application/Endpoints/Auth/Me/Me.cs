using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Identity.Application.Contracts;

namespace Ulbra.One.Identity.Application.Endpoints.Auth.Me;

public sealed class Me : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/me", Handle)
            .WithName("Me")
            .WithTags("Auth")
            .WithSummary("Returns the caller's identity and permissions.")
            .WithDescription("Who the bearer token belongs to: name, e-mail, profile photo, role, "
                + "effective permissions, the companies in scope, and whether the user may approve budget "
                + "adjustments. The frontend drives its menu and route guards from this. Most of it is read "
                + "from the token itself, so a role or scope change only shows up here after the user signs "
                + "in again — the budget-adjustment approval flag and the photo are the exceptions, read "
                + "live.")
            .RequireAuthorization()
            .Produces<MeResponse>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        UserContext user, IApprovalAuthority authority, IUserDirectory users, CancellationToken ct)
    {
        var userId = Guid.Parse(user.UserId);

        // The budget-adjustment approval grant is stored per-user and read live (not in the JWT), so resolve it
        // here for the UI hint. Admin and Presidency always may approve (they bypass the per-user grant). See AEL-883.
        var canApproveBudgetAdjustment = user.IsAdmin || user.IsPresident
            || (await authority.GetAsync(userId, ct))?.CanApproveBudgetAdjustment == true;

        // Read live rather than carried as a token claim: the photo changes on the Google side without a new
        // sign-in, and a rotated URL in a long-lived token would render as a broken image.
        var profiles = await users.GetProfilesAsync([userId], ct);
        var pictureUrl = profiles.TryGetValue(userId, out var profile) ? profile.PictureUrl : null;

        return TypedResults.Ok(new MeResponse(
            new UserId(userId), user.UserName, user.UserEmail, pictureUrl, user.Role, user.IsAdmin,
            user.Permissions, user.Scopes.Select(s => new CompanyId(s.CompanyId)).ToArray(), canApproveBudgetAdjustment));
    }
}
