using Ulbra.One.Identity.Application;
using Xunit;

namespace Ulbra.One.UnitTests.Identity;

public class GoogleAuthOptionsTests
{
    private static GoogleAuthOptions Options() => new()
    {
        AllowedDomains = ["ulbra.br", "aelbra.com.br"],
        BootstrapAdmins = ["bootstrap.admin@example.com"],
    };

    [Fact]
    public void BootstrapAdmin_bypasses_the_domain_allow_list()
    {
        // gmail.com is NOT in AllowedDomains, but the email is a bootstrap admin.
        Assert.True(Options().IsEmailAllowed("bootstrap.admin@example.com"));
    }

    [Fact]
    public void Allowed_domain_is_permitted()
    {
        Assert.True(Options().IsEmailAllowed("alguem@ulbra.br"));
    }

    [Fact]
    public void Disallowed_domain_non_bootstrap_is_rejected()
    {
        Assert.False(Options().IsEmailAllowed("redacted-07@example.invalid"));
    }

    [Fact]
    public void Empty_allow_list_permits_any_email()
    {
        var opt = new GoogleAuthOptions { AllowedDomains = [], BootstrapAdmins = [] };
        Assert.True(opt.IsEmailAllowed("qualquer@dominio.com"));
    }

    [Fact]
    public void IsBootstrapAdmin_is_case_insensitive()
    {
        Assert.True(Options().IsBootstrapAdmin("Bootstrap.Admin@Example.com"));
        Assert.False(Options().IsBootstrapAdmin("outro@ulbra.br"));
    }
}
