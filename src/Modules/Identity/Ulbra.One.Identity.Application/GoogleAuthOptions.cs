using System;
using System.Linq;

namespace Ulbra.One.Identity.Application;

public sealed class GoogleAuthOptions
{
    public const string SectionName = "Authentication:Google";

    public string ClientId { get; set; } = string.Empty;
    /// <summary>Legacy domain allow-list. See <see cref="IsEmailAllowed"/>: it no longer gates sign-in.</summary>
    public string[] AllowedDomains { get; set; } = [];
    public string[] BootstrapAdmins { get; set; } = [];

    /// <summary>True when <paramref name="email"/> is an explicitly-listed bootstrap admin.</summary>
    public bool IsBootstrapAdmin(string email) =>
        BootstrapAdmins.Contains(email, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the email's domain is on the allow-list. Bootstrap admins always pass; an empty
    /// allow-list permits any verified Google account.
    /// <para>No longer gates Google sign-in: login is invitation-only, and registering an e-mail
    /// IS the authorization — a registered consultant on gmail.com signs in all the same. Kept so the
    /// existing <c>Authentication:Google:AllowedDomains</c> configuration stays valid; slated for removal
    /// once no environment sets it.</para></summary>
    public bool IsEmailAllowed(string email)
    {
        if (IsBootstrapAdmin(email)) return true;
        if (AllowedDomains.Length == 0) return true;
        var at = email.IndexOf('@');
        var domain = at >= 0 ? email[(at + 1)..] : string.Empty;
        return AllowedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase);
    }
}
