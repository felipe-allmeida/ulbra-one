using Testcontainers.PostgreSql;

namespace Ulbra.One.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>The single, suite-wide API host. Booted once in <see cref="InitializeAsync"/>
    /// (8-module DI build + migrations + dev seed + bus start) and reused by every test in the
    /// "Postgres" collection, instead of one <see cref="ApiFactory"/> boot per test method.
    /// Tests that need per-test service overrides derive their own host via WithWebHostBuilder.</summary>
    public ApiFactory Api { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Api = new ApiFactory(ConnectionString);
        // Force the host to build now so the one-time cost (DI, migrations, seed, bus start) is paid
        // up front and deterministically, instead of lazily inside whichever test touches it first.
        _ = Api.Services;
    }

    public async Task DisposeAsync()
    {
        if (Api is not null)
            await Api.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
