namespace Ulbra.One.Identity.Domain;

/// <summary>
/// Nomes dos cargos (roles) semeados. O usuário guarda o cargo por nome (<c>User.Role</c>);
/// as permissões de cada cargo vivem em <c>Role.Claims</c> e são semeadas na Infraestrutura.
/// <c>Admin</c> é superusuário (bypass global de permissões).
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";
    public const string Financeiro = "Financeiro";
    public const string Controladoria = "Controladoria";
    public const string Contabil = "Contabil";
}
