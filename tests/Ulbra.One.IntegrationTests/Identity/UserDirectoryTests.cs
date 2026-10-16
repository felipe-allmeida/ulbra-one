using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.IntegrationTests.Identity;

[Collection("Postgres")]
public class UserDirectoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task GetNamesAsync_resolves_known_ids_and_omits_unknown()
    {
        var factory = fixture.Api;
        // CreateAdminClientAsync provisions the admin user row (admin@ulbra.br / "Admin Teste").
        await factory.CreateAdminClientAsync();

        using var scope = factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        var admin = await uow.ReadSet<User>().FirstAsync(u => u.Email == "admin@ulbra.br");
        var unknown = Guid.NewGuid();

        var names = await directory.GetNamesAsync(new[] { admin.Id, unknown }, CancellationToken.None);

        Assert.Equal("Admin Teste", names[admin.Id]);
        Assert.False(names.ContainsKey(unknown));
    }

    [Fact]
    public async Task GetNamesAsync_empty_input_returns_empty()
    {
        var factory = fixture.Api;
        await factory.CreateAdminClientAsync();
        using var scope = factory.Services.CreateScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        var names = await directory.GetNamesAsync(Array.Empty<Guid>(), CancellationToken.None);

        Assert.Empty(names);
    }
}
