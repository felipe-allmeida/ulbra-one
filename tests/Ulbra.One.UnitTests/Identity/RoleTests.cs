using Ulbra.One.Identity.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Identity;

public class RoleTests
{
    [Fact]
    public void Grant_adds_a_claim()
    {
        var role = new Role(Guid.NewGuid(), "Finance");

        role.Grant("payables:read");

        Assert.Single(role.Claims);
        Assert.Contains(role.Claims, c => c.Permission == "payables:read" && c.RoleId == role.Id);
    }

    [Fact]
    public void Grant_is_idempotent()
    {
        var role = new Role(Guid.NewGuid(), "Finance");

        role.Grant("payables:read");
        role.Grant("payables:read");

        Assert.Single(role.Claims);
    }

    [Fact]
    public void Revoke_removes_the_claim()
    {
        var role = new Role(Guid.NewGuid(), "Finance");
        role.Grant("payables:read");

        role.Revoke("payables:read");

        Assert.Empty(role.Claims);
    }
}
