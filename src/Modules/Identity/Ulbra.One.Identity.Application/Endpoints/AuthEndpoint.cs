using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Infrastructure.Authentication;
using Ulbra.One.Identity.Application.Contracts;
using Ulbra.One.Identity.Domain;
using Ulbra.One.Identity.Domain.Entities;
using Ulbra.One.Identity.Infrastructure;
using Ulbra.One.Identity.Infrastructure.Database;
using Ulbra.One.Identity.Infrastructure.Services;

namespace Ulbra.One.Identity.Application.Endpoints;

public sealed class AuthEndpoint : IEndpoint
{
    public record GoogleSignInRequest(string IdToken);
    public record DevTokenRequest(string? Email, string? Name);

    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/google", GoogleSignIn).WithName("GoogleSignIn")
            .RequireRateLimiting("auth")
            .Produces<LoginResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status501NotImplemented);
        group.MapPost("/dev-token", DevToken).WithName("DevToken")
            .RequireRateLimiting("auth")
            .Produces<LoginResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/me", Me).WithName("Me").RequireAuthorization()
            .Produces<MeResponse>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> GoogleSignIn(
        GoogleSignInRequest req,
        IGoogleTokenValidator validator,
        IOptions<GoogleAuthOptions> options,
        IdentityContext db,
        JwtTokenService jwt,
        CancellationToken ct)
    {
        var opt = options.Value;
        if (string.IsNullOrWhiteSpace(opt.ClientId))
            return Results.Problem(detail: "Google ClientId is not configured.", statusCode: StatusCodes.Status501NotImplemented, title: "Auth.GoogleNotConfigured");

        GoogleTokenPayload payload;
        try
        {
            payload = await validator.ValidateAsync(req.IdToken, opt.ClientId, ct);
        }
        catch (Exception)
        {
            return Results.Problem(detail: "Invalid Google token.", statusCode: StatusCodes.Status401Unauthorized, title: "Auth.GoogleTokenInvalid");
        }

        if (!payload.EmailVerified)
            return Results.Problem(detail: "Email not verified by Google.", statusCode: StatusCodes.Status401Unauthorized, title: "Auth.EmailNotVerified");

        var email = payload.Email.ToLowerInvariant();

        if (opt.AllowedDomains.Length > 0)
        {
            var domain = email.Contains('@') ? email[(email.IndexOf('@') + 1)..] : string.Empty;
            if (!opt.AllowedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
                return Results.Problem(detail: $"Domain '{domain}' is not allowed.", statusCode: StatusCodes.Status403Forbidden, title: "Auth.DomainNotAllowed");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        var isBootstrapAdmin = opt.BootstrapAdmins.Contains(email, StringComparer.OrdinalIgnoreCase);

        if (user is null)
        {
            user = new User(email, payload.Name, isBootstrapAdmin ? Roles.Admin : Roles.User);
            db.Users.Add(user);
        }
        else if (isBootstrapAdmin && user.Role != Roles.Admin)
        {
            user.SetRole(Roles.Admin);
        }

        user.RegisterGoogleLogin(payload.Subject, payload.Name);
        await db.SaveChangesAsync(ct);

        var token = jwt.GenerateToken(user.Id.ToString(), user.Role, user.Name, user.Email);
        return Results.Ok(new LoginResponse(token, new UserSummary(user.Id, user.Email, user.Name, user.Role)));
    }

    private static async Task<IResult> DevToken(
        DevTokenRequest req,
        IHostEnvironment env,
        IdentityContext db,
        JwtTokenService jwt,
        CancellationToken ct)
    {
        if (!env.IsDevelopment())
            return Results.NotFound();

        var email = (req.Email ?? "dev@ulbra.br").ToLowerInvariant();
        var name = req.Name ?? "Dev User";

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            user = new User(email, name, Roles.Admin);
            db.Users.Add(user);
        }
        user.RegisterLogin();
        await db.SaveChangesAsync(ct);

        var token = jwt.GenerateToken(user.Id.ToString(), user.Role, user.Name, user.Email);
        return Results.Ok(new LoginResponse(token, new UserSummary(user.Id, user.Email, user.Name, user.Role)));
    }

    private static IResult Me(UserContext user) =>
        Results.Ok(new MeResponse(user.UserId, user.UserName, user.UserEmail, user.Role, user.IsAdmin));
}
