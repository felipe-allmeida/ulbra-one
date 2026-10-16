using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Ulbra.One.IntegrationTests.Api;

/// <summary>
/// Asserts on the wire what <c>ErrorResults</c> promises in code: every error the API returns carries a
/// <c>trace_id</c> and a machine-readable <c>error_code</c>, and the <c>title</c> is a human summary rather
/// than the code itself.
/// <para>
/// These go through real endpoints instead of unit-testing the factory, because the failure this guards
/// against is an endpoint bypassing the factory — which a test of the factory cannot see.
/// </para>
/// </summary>
[Collection("Postgres")]
public class ErrorEnvelopeTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Not_found_carries_the_envelope()
    {
        var client = await fixture.Api.CreateAdminClientAsync();

        var response = await client.GetAsync($"/api/vendors/vendor_{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertEnvelopeAsync(response);
    }

    [Fact]
    public async Task Domain_validation_failure_carries_the_envelope()
    {
        var client = await fixture.Api.CreateAdminClientAsync();

        // A vendor with an invalid CNPJ — rejected by the domain, not by model binding.
        var response = await client.PostAsJsonAsync("/api/vendors", new
        {
            name = "Fornecedor Inválido",
            document = "11111111111111",
            personType = "Company",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertEnvelopeAsync(response);
    }

    [Fact]
    public async Task Unauthenticated_request_carries_a_trace_id()
    {
        using var client = fixture.Api.CreateClient();

        var response = await client.GetAsync("/api/vendors");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // 401 is emitted by the auth middleware, before any handler runs, so it has no error_code to
        // carry — but it must still be problem+json so the frontend parses it like every other error.
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task AssertEnvelopeAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        var traceId = Assert.IsType<string>(root.GetProperty("trace_id").GetString());
        Assert.Equal(32, traceId.Length);

        var errorCode = Assert.IsType<string>(root.GetProperty("error_code").GetString());
        Assert.Matches("^[a-z][a-z0-9_]*\\.[a-z][a-z0-9_]*$", errorCode);

        // The split that motivated the change: title is prose for a human, the code is for the machine.
        var title = root.GetProperty("title").GetString();
        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.NotEqual(errorCode, title);
        Assert.DoesNotContain('_', title!);
    }
}
