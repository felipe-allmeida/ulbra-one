namespace Ulbra.One.Identity.Application;

public sealed class GoogleAuthOptions
{
    public const string SectionName = "Authentication:Google";

    public string ClientId { get; set; } = string.Empty;
    public string[] AllowedDomains { get; set; } = [];
    public string[] BootstrapAdmins { get; set; } = [];
}
