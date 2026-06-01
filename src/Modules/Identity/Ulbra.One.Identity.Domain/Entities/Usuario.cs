namespace Ulbra.One.Identity.Domain.Entities;

/// <summary>
/// Usuário autenticado via Google Workspace. Papéis: Admin | User.
/// </summary>
public class Usuario
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Email { get; private set; } = string.Empty;
    public string Nome { get; private set; } = string.Empty;
    public string Role { get; private set; } = Roles.User;
    public string? GoogleSubject { get; private set; }
    public bool Ativo { get; private set; } = true;
    public DateTime CriadoEm { get; private set; } = DateTime.UtcNow;
    public DateTime? UltimoLoginEm { get; private set; }

    private Usuario() { }

    public Usuario(string email, string nome, string role)
    {
        Email = email;
        Nome = nome;
        Role = role;
    }

    public void RegistrarLoginGoogle(string googleSubject, string nome)
    {
        GoogleSubject = googleSubject;
        if (!string.IsNullOrWhiteSpace(nome)) Nome = nome;
        UltimoLoginEm = DateTime.UtcNow;
    }

    public void RegistrarLogin() => UltimoLoginEm = DateTime.UtcNow;

    public void DefinirRole(string role) => Role = role;
}
