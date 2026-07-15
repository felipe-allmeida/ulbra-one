using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Identity.Application.Contracts;
using Ulbra.One.Identity.Domain;
using Ulbra.One.Identity.Domain.Entities;
using Ulbra.One.Identity.Application.Services;

namespace Ulbra.One.Identity.Application.Endpoints.Auth.GoogleSignIn;

public sealed class GoogleSignIn : IEndpoint
{
    public sealed record Request(string IdToken);

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/google", Handle)
            .WithName("GoogleSignIn")
            .WithTags("Auth")
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
            return TypedResults.Problem(detail: "Google ClientId is not configured.", statusCode: StatusCodes.Status501NotImplemented, title: "Auth.GoogleNotConfigured");

        GoogleTokenPayload payload;
        try
        {
            payload = await validator.ValidateAsync(req.IdToken, opt.ClientId, ct);
        }
        catch (Exception)
        {
            return TypedResults.Problem(detail: "Invalid Google token.", statusCode: StatusCodes.Status401Unauthorized, title: "Auth.GoogleTokenInvalid");
        }

        if (!payload.EmailVerified)
            return TypedResults.Problem(detail: "Email not verified by Google.", statusCode: StatusCodes.Status401Unauthorized, title: "Auth.EmailNotVerified");

        var email = payload.Email.ToLowerInvariant();

        // Bootstrap admins bypass the domain allow-list (see GoogleAuthOptions.IsEmailAllowed).
        if (!opt.IsEmailAllowed(email))
            return TypedResults.Problem(detail: $"Email '{email}' is not allowed.", statusCode: StatusCodes.Status403Forbidden, title: "Auth.DomainNotAllowed");

        var user = await uow.Set<User>().FirstOrDefaultAsync(u => u.Email == email, ct);
        var isBootstrapAdmin = opt.IsBootstrapAdmin(email);

        if (user is null)
        {
            user = new User(email, payload.Name, isBootstrapAdmin ? Roles.Admin : Roles.User);
            await uow.AddAsync(user, ct);
        }
        else if (isBootstrapAdmin && user.Role != Roles.Admin)
        {
            user.SetRole(Roles.Admin);
        }

        user.RegisterGoogleLogin(payload.Subject, payload.Name, clock.GetUtcNow());
        await uow.SaveChangesAsync(ct);

        var permissions = await uow.Set<Role>()
            .Where(r => r.Name == user.Role)
            .SelectMany(r => r.Claims)
            .Select(c => c.Permission)
            .ToListAsync(ct);

        var token = jwt.GenerateToken(user.Id.ToString(), user.Role, user.Name, user.Email, permissions);
        return TypedResults.Ok(new LoginResponse(token, new UserSummary(new UserId(user.Id), user.Email, user.Name, user.Role)));
    }
}
