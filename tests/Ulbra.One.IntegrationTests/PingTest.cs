using System.Net;

namespace Ulbra.One.IntegrationTests;

[Collection("Postgres")]
public class PingTest(PostgresFixture fixture)
{
    [Fact]
    public async Task Ping_Returns_Ok()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var res = await factory.CreateClient().GetAsync("/api/ping");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
