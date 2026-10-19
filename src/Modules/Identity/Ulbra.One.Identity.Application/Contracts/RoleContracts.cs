using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Identity.Application.Contracts;

/// <summary>Um cargo e suas permissões efetivas — para a tela de administração de acessos.</summary>
[SwaggerSchema("A role and the permissions it carries. Roles are seeded, not created through the API — "
    + "this endpoint exists to read them and to edit their permission set.")]
public sealed record RoleResponse(
    RoleId Id,
    [property: SwaggerSchema("Role name. It is the key a user references, so renaming one is a breaking "
        + "change for every user pointing at it.")]
    [property: SwaggerExample("Controladoria")]
    string Name,
    [property: SwaggerSchema("Permissions the role grants, in `resource:action` form. Admin comes back "
        + "with an empty list because it bypasses the check rather than holding every permission.")]
    IReadOnlyCollection<string> Permissions);

/// <summary>Define o conjunto completo de permissões de um cargo (substitui o atual).</summary>
public sealed record SetRolePermissionsRequest(
    [property: SwaggerSchema("The role's complete permission set, in `resource:action` form. This "
        + "REPLACES the current set, so a permission left out is revoked — send the full list, not just "
        + "the additions. Every value must exist in the catalogue returned by GET /api/permissions; an "
        + "unknown one returns 400.")]
    IReadOnlyList<string> Permissions);
