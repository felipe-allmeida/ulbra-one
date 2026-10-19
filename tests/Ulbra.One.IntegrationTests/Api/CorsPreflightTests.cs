using System.Net.Http;
using Xunit;

namespace Ulbra.One.IntegrationTests.Api;

[Collection("Postgres")]
public class CorsPreflightTests(PostgresFixture fixture)
{
    // Matches AllowedOrigins in appsettings.Development.json, the environment the test host runs as.
    private const string AllowedOrigin = "http://localhost:5173";

    [Fact]
    public async Task Preflight_tells_the_browser_how_long_it_may_cache_the_answer()
    {
        var client = fixture.Api.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Options, "/api/notifications/unread-count");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await client.SendAsync(request);

        Assert.True(
            response.Headers.TryGetValues("Access-Control-Max-Age", out var values),
            "The preflight response carries no Access-Control-Max-Age, so the browser will reissue " +
            "an OPTIONS before every single request.");

        var maxAge = int.Parse(Assert.Single(values!));
        Assert.True(maxAge > 0, $"Access-Control-Max-Age must be positive; got {maxAge}.");

        // Chrome ignores anything above 7200s, so a larger value is a number no browser honours.
        Assert.True(maxAge <= 7200, $"Access-Control-Max-Age of {maxAge}s exceeds Chrome's 7200s cap.");
    }

    [Fact]
    public async Task Preflight_from_an_unknown_origin_is_not_answered()
    {
        var client = fixture.Api.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Options, "/api/notifications/unread-count");
        request.Headers.Add("Origin", "https://nao-autorizado.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Max-Age"));
    }
}
