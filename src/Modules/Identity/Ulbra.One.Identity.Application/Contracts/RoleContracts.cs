using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Identity.Application.Contracts;

/// <summary>Um cargo e suas permissões efetivas — para a tela de administração de acessos.</summary>
public sealed record RoleResponse(RoleId Id, string Name, IReadOnlyCollection<string> Permissions);

/// <summary>Define o conjunto completo de permissões de um cargo (substitui o atual).</summary>
public sealed record SetRolePermissionsRequest(IReadOnlyList<string> Permissions);
