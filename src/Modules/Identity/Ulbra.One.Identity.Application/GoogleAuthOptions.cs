using System;
using System.Linq;

namespace Ulbra.One.Identity.Application;

public sealed class GoogleAuthOptions
{
    public const string SectionName = "Authentication:Google";

    public string ClientId { get; set; } = string.Empty;
    public string[] AllowedDomains { get; set; } = [];
    public string[] BootstrapAdmins { get; set; } = [];

    /// <summary>True when <paramref name="email"/> is an explicitly-listed bootstrap admin.</summary>
    public bool IsBootstrapAdmin(string email) =>
        BootstrapAdmins.Contains(email, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the email may sign in. Bootstrap admins always may (they bypass the domain
    /// allow-list); everyone else must match an <see cref="AllowedDomains"/> entry. An empty
    /// allow-list permits any verified Google account.</summary>
    public bool IsEmailAllowed(string email)
    {
        if (IsBootstrapAdmin(email)) return true;
        if (AllowedDomains.Length == 0) return true;
        var at = email.IndexOf('@');
        var domain = at >= 0 ? email[(at + 1)..] : string.Empty;
        return AllowedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase);
    }
}
