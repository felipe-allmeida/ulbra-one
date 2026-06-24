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
            .RequireAuthorization()
            .Produces<MeResponse>(StatusCodes.Status200OK);
    }

    private static IResult Handle(UserContext user) =>
        TypedResults.Ok(new MeResponse(new UserId(Guid.Parse(user.UserId)), user.UserName, user.UserEmail, user.Role, user.IsAdmin));
}
