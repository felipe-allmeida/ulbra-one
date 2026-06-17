using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Identity.Domain.Entities;

/// <summary>User authenticated via Google Workspace. Roles: Admin | User.</summary>
public class User : IAuditable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Email { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Role { get; private set; } = Roles.User;
    public string? GoogleSubject { get; private set; }
    public bool Active { get; private set; } = true;
    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private User() { }

    public User(string email, string name, string role)
    {
        Email = email;
        Name = name;
        Role = role;
    }

    public void RegisterGoogleLogin(string googleSubject, string name, DateTimeOffset at)
    {
        GoogleSubject = googleSubject;
        if (!string.IsNullOrWhiteSpace(name)) Name = name;
        LastLoginAt = at;
    }

    public void RegisterLogin(DateTimeOffset at) => LastLoginAt = at;

    public void SetRole(string role) => Role = role;

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
