using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Ulbra.One.IntegrationTests;

public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Host configuration has the highest precedence, so this overrides the value in
        // appsettings.Development.json (which Program reads eagerly at startup). Pointing at the
        // ephemeral test container keeps the suite isolated from the dev/compose database.
        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = connectionString,
            }));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
    }

    public async Task<HttpClient> CreateAdminClientAsync(string email = "admin@ulbra.br", string nome = "Admin Teste")
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/dev-token", new { email, name = nome });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    private sealed record TokenResponse(string Token);
}
