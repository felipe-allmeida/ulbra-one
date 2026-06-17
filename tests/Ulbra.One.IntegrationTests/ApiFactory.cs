using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Ulbra.One.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string? _rabbitHost;
    private readonly int? _rabbitPort;
    private readonly string? _rabbitUser;
    private readonly string? _rabbitPassword;

    /// <summary>Postgres-only constructor; bus connects lazily (no broker needed for most tests).</summary>
    public ApiFactory(string connectionString)
        : this(connectionString, null, null, null, null) { }

    /// <summary>Full constructor with RabbitMQ config for end-to-end eventing tests.</summary>
    public ApiFactory(string connectionString, string? rabbitHost, int? rabbitPort, string? rabbitUser, string? rabbitPassword)
    {
        _connectionString = connectionString;
        _rabbitHost = rabbitHost;
        _rabbitPort = rabbitPort;
        _rabbitUser = rabbitUser;
        _rabbitPassword = rabbitPassword;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Host configuration has the highest precedence, so this overrides the value in
        // appsettings.Development.json (which Program reads eagerly at startup). Pointing at the
        // ephemeral test container keeps the suite isolated from the dev/compose database.
        builder.ConfigureHostConfiguration(config =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = _connectionString,
            };

            if (_rabbitHost is not null)
            {
                dict["MassTransit:Host"] = _rabbitHost;
                dict["MassTransit:Port"] = _rabbitPort?.ToString();
                dict["MassTransit:User"] = _rabbitUser;
                dict["MassTransit:Password"] = _rabbitPassword;
            }

            config.AddInMemoryCollection(dict);
        });
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
    }

    public async Task<HttpClient> CreateAdminClientAsync(string email = "admin@ulbra.br", string name = "Admin Teste")
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/dev-token", new { email, name });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    private sealed record TokenResponse(string Token);
}
