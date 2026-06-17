using Testcontainers.RabbitMq;

namespace Ulbra.One.IntegrationTests;

public sealed class RabbitMqFixture : IAsyncLifetime
{
    public const string DefaultUsername = "guest";
    public const string DefaultPassword = "guest";

    private readonly RabbitMqContainer _container = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-management-alpine")
        .WithUsername(DefaultUsername)
        .WithPassword(DefaultPassword)
        .Build();

    /// <summary>The host accessible from the test process (always localhost for mapped ports).</summary>
    public string Host => "localhost";
    public int Port => _container.GetMappedPublicPort(5672);
    public string Username => DefaultUsername;
    public string Password => DefaultPassword;

    public async Task InitializeAsync() => await _container.StartAsync();
    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("PostgresRabbitMq")]
public sealed class PostgresRabbitMqCollection
    : ICollectionFixture<PostgresFixture>, ICollectionFixture<RabbitMqFixture>;
