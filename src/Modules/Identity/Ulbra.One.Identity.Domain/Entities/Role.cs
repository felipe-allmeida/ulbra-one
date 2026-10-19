namespace Ulbra.One.Identity.Domain.Entities;

/// <summary>
/// Cargo funcional (ex.: Financeiro, Controladoria, Contábil). Agrupa um conjunto de permissões
/// (<see cref="RoleClaim"/>) que são emitidas como claims no JWT do usuário. Usuários referenciam
/// o cargo pelo <see cref="Name"/> (<c>User.Role</c>), mantendo o acoplamento mínimo.
/// </summary>
public class Role
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;

    private readonly List<RoleClaim> _claims = [];
    public IReadOnlyCollection<RoleClaim> Claims => _claims;

    private Role() { }

    public Role(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>Concede uma permissão ao cargo (idempotente).</summary>
    public void Grant(string permission)
    {
        if (_claims.Exists(c => c.Permission == permission)) return;
        _claims.Add(new RoleClaim(Id, permission));
    }

    /// <summary>Remove uma permissão do cargo.</summary>
    public void Revoke(string permission) =>
        _claims.RemoveAll(c => c.Permission == permission);
}
