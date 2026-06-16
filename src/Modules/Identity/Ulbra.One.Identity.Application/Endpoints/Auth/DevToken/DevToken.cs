using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Identity.Application.Contracts;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Identity.Domain;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Application.Endpoints.Auth.DevToken;

public sealed class DevToken : IEndpoint
{
    public sealed record Request
    {
        [NormalizeText] public string? Email { get; set; }
        [NormalizeText] public string? Name { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/dev-token", Handle)
            .WithName("DevToken")
            .WithTags("Auth")
            .RequireRateLimiting("auth")
            .WithRequestNormalizer<Request>()
            .Produces<LoginResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        Request req,
        IHostEnvironment env,
        [FromKeyedServices("identity")] IUnitOfWork uow,
        ITokenService jwt,
        CancellationToken ct)
    {
        if (!env.IsDevelopment())
            return Results.NotFound();

        var email = (req.Email ?? "dev@ulbra.br").ToLowerInvariant();
        var name = req.Name ?? "Dev User";

        var user = await uow.Set<User>().FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            user = new User(email, name, Roles.Admin);
            await uow.AddAsync(user, ct);
            user.RegisterLogin();
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
                user.RegisterLogin();
                await uow.SaveChangesAsync(ct);
            }
        }
        else
        {
            user.RegisterLogin();
            await uow.SaveChangesAsync(ct);
        }

        var token = jwt.GenerateToken(user.Id.ToString(), user.Role, user.Name, user.Email);
        return Results.Ok(new LoginResponse(token, new UserSummary(new UserId(user.Id), user.Email, user.Name, user.Role)));
    }
}
