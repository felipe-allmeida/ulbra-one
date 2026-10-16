using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Identity.Application.Contracts;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Application.Endpoints.Users.CreateUser;

/// <summary>Pre-registers a user by e-mail (convite). Sign-in is invitation-only, so this is how a person
/// comes into existence: the admin registers the e-mail with the role — and optionally the scope and approval
/// authority — and the first Google login binds to this row, already with the right access. Admin-only.</summary>
public sealed class CreateUser : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle)
            .WithName("CreateUser")
            .WithTags("Users")
            .WithSummary("Pre-registers a user by e-mail.")
            .WithDescription("Creates the user before their first sign-in, already carrying the role, the "
                + "scope and the approval authority they should have — so the first Google login lands on "
                + "the intended access instead of a default one. Until that login the row is listed with "
                + "pendingFirstLogin = true. Registering is the authorization: the e-mail may sign in even "
                + "if its domain is not on the AllowedDomains list. An e-mail already registered returns "
                + "409 (user.email_already_registered); an unknown role or a malformed e-mail returns 400.")
            .RequirePermission(Permissions.AdminUsers)
            .Produces<UserAdminResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(CreateUserRequest req, IUnitOfWork uow, CancellationToken ct)
    {
        var email = (req.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (!IsValidEmail(email))
            return Invalid("A valid e-mail is required.", ErrorCodes.User.EmailInvalid);

        if (string.IsNullOrWhiteSpace(req.Role))
            return Invalid("Role is required.", ErrorCodes.User.RoleInvalid);
        if (!await uow.ReadSet<Role>().AnyAsync(r => r.Name == req.Role, ct))
            return Invalid($"Role '{req.Role}' does not exist.", ErrorCodes.User.RoleInvalid);

        if (req.ApprovalLimit is < 0)
            return Invalid("ApprovalLimit cannot be negative.", ErrorCodes.User.ApprovalLimitInvalid);

        var scopeRequest = new SetUserScopeRequest(req.CompanyIds ?? [], req.Units, req.CostCenters);
        if (UserScopeWriter.Validate(scopeRequest, out var scope) is { } scopeInvalid) return scopeInvalid;

        if (await uow.ReadSet<User>().AnyAsync(u => u.Email == email, ct))
            return ErrorResults.InvalidState(
                $"E-mail '{email}' is already registered.", ErrorCodes.User.EmailAlreadyRegistered);

        // Name is optional at registration: Google's own name overwrites it on the first sign-in.
        var user = new User(email, req.Name?.Trim() ?? string.Empty, req.Role);
        if (req.ApprovalLimit is not null) user.SetApprovalAuthority(req.ApprovalLimit, hasApprovalAutonomy: false);
        if (req.CanApproveBudgetAdjustment) user.SetCanApproveBudgetAdjustment(true);
        await uow.AddAsync(user, ct);

        await UserScopeWriter.ApplyAsync(uow, user.Id, scope, ct);
        await uow.SaveChangesAsync(ct);

        var response = await UserAdminResponseFactory.BuildAsync(uow, user, ct);
        return TypedResults.Created($"/api/users/{response.Id}", response);
    }

    /// <summary>Shape check only — the real proof that the address exists is the Google sign-in itself.</summary>
    private static bool IsValidEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0
            && at == email.LastIndexOf('@')
            && at < email.Length - 1
            && email.IndexOf('.', at) > at + 1
            && !email.EndsWith('.')
            && !email.Any(char.IsWhiteSpace);
    }

    private static IResult Invalid(string detail, string errorCode) =>
        ErrorResults.InvalidRequest(detail, errorCode);
}
