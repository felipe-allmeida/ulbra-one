namespace Ulbra.One.Identity.Domain.Entities;

/// <summary>Uma permissão (formato <c>recurso:ação</c>) concedida a um <see cref="Role"/>.</summary>
public class RoleClaim
{
    public Guid RoleId { get; private set; }
    public string Permission { get; private set; } = string.Empty;

    private RoleClaim() { }

    public RoleClaim(Guid roleId, string permission)
    {
        RoleId = roleId;
        Permission = permission;
    }
}
