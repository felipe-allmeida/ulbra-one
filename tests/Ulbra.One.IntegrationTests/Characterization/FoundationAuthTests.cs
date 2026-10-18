using System.Net;

namespace Ulbra.One.IntegrationTests.Characterization;

[Collection("Postgres")]
public class FoundationAuthTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Anonymous_Companies_Returns_401()
    {
        var factory = fixture.Api;
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/companies");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Unknown_Route_Anonymous_Returns_401_NotLeakingExistence()
    {
        // Deny-by-default (AEL-764): the fallback policy runs before routing, so an anonymous caller can't
        // tell an unknown route from a protected one — 401, not a 404 that would confirm the route is absent.
        var factory = fixture.Api;
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/this-does-not-exist");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Unknown_Route_Authenticated_Returns_404_ProblemDetails()
    {
        // Authenticated: routing runs, and an unmatched route yields 404 as problem+json (UseStatusCodePages).
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var res = await client.GetAsync("/api/this-does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }
}
