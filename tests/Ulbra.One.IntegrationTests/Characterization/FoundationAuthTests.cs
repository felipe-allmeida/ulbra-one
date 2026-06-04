using System.Net;

namespace Ulbra.One.IntegrationTests.Characterization;

[Collection("Postgres")]
public class FoundationAuthTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Anonymous_Companies_Returns_401()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/companies");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Unknown_Route_Returns_ProblemDetails_ContentType()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/this-does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }
}
