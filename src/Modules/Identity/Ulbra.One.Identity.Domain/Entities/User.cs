namespace Ulbra.One.Identity.Domain.Entities;

/// <summary>Usuário autenticado via Google Workspace. Papéis: Admin | User.</summary>
public class User
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Email { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Role { get; private set; } = Roles.User;
    public string? GoogleSubject { get; private set; }
    public bool Active { get; private set; } = true;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; private set; }

    private User() { }

    public User(string email, string name, string role)
    {
        Email = email;
        Name = name;
        Role = role;
    }

    public void RegisterGoogleLogin(string googleSubject, string name)
    {
        GoogleSubject = googleSubject;
        if (!string.IsNullOrWhiteSpace(name)) Name = name;
        LastLoginAt = DateTime.UtcNow;
    }

    public void RegisterLogin() => LastLoginAt = DateTime.UtcNow;

    public void SetRole(string role) => Role = role;
}
