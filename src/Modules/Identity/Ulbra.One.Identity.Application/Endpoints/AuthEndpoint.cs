using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Infrastructure.Authentication;
using Ulbra.One.Identity.Domain;
using Ulbra.One.Identity.Domain.Entities;
using Ulbra.One.Identity.Infrastructure;
using Ulbra.One.Identity.Infrastructure.Database;
using Ulbra.One.Identity.Infrastructure.Services;

namespace Ulbra.One.Identity.Application.Endpoints;

public sealed class AuthEndpoint : IEndpoint
{
    public record GoogleSignInRequest(string IdToken);
    public record DevTokenRequest(string? Email, string? Nome);

    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/google", GoogleSignIn).WithName("GoogleSignIn");
        group.MapPost("/dev-token", DevToken).WithName("DevToken");
        group.MapGet("/me", Me).WithName("Me").RequireAuthorization();
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
            return Results.Problem("Google ClientId não configurado.", statusCode: StatusCodes.Status501NotImplemented);

        GoogleTokenPayload payload;
        try
        {
            payload = await validator.ValidateAsync(req.IdToken, opt.ClientId, ct);
        }
        catch (Exception)
        {
            return Results.Problem("Token Google inválido.", statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!payload.EmailVerified)
            return Results.Problem("E-mail não verificado pelo Google.", statusCode: StatusCodes.Status401Unauthorized);

        var email = payload.Email.ToLowerInvariant();

        if (opt.AllowedDomains.Length > 0)
        {
            var domain = email.Contains('@') ? email[(email.IndexOf('@') + 1)..] : string.Empty;
            if (!opt.AllowedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
                return Results.Problem($"Domínio '{domain}' não autorizado.", statusCode: StatusCodes.Status403Forbidden);
        }

        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Email == email, ct);
        var isBootstrapAdmin = opt.BootstrapAdmins.Contains(email, StringComparer.OrdinalIgnoreCase);

        if (usuario is null)
        {
            usuario = new Usuario(email, payload.Name, isBootstrapAdmin ? Roles.Admin : Roles.User);
            db.Usuarios.Add(usuario);
        }
        else if (isBootstrapAdmin && usuario.Role != Roles.Admin)
        {
            usuario.DefinirRole(Roles.Admin);
        }

        usuario.RegistrarLoginGoogle(payload.Subject, payload.Name);
        await db.SaveChangesAsync(ct);

        var token = jwt.GenerateToken(usuario.Id.ToString(), usuario.Role, usuario.Nome, usuario.Email);
        return Results.Ok(new { token, usuario = new { usuario.Id, usuario.Email, usuario.Nome, usuario.Role } });
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
        var nome = req.Nome ?? "Usuário Dev";

        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (usuario is null)
        {
            usuario = new Usuario(email, nome, Roles.Admin);
            db.Usuarios.Add(usuario);
        }
        usuario.RegistrarLogin();
        await db.SaveChangesAsync(ct);

        var token = jwt.GenerateToken(usuario.Id.ToString(), usuario.Role, usuario.Nome, usuario.Email);
        return Results.Ok(new { token, usuario = new { usuario.Id, usuario.Email, usuario.Nome, usuario.Role } });
    }

    private static IResult Me(UserContext user) =>
        Results.Ok(new
        {
            userId = user.UserId,
            nome = user.UserName,
            email = user.UserEmail,
            role = user.Role,
            isAdmin = user.IsAdmin
        });
}
