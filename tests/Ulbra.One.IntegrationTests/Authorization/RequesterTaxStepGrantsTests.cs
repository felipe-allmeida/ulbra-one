using System.Net;
using Ulbra.One.Identity.Domain; // Roles

namespace Ulbra.One.IntegrationTests.Authorization;

/// <summary>
/// The SEEDED grants of the Requester role (<c>RoleSeed</c> via <c>HasData</c>), not a hand-picked token:
/// these sign in through dev-token so the permissions come from <c>role_claim</c>, exactly like a real login.
/// The requester registers a vendor inline and fills the tax step of a service/conta-fixa requisition, so the
/// two reference reads that screen performs must answer — in production both returned 403, and the vendor was
/// saved with no associated ledger account and no withholding preview.
/// </summary>
[Collection("Postgres")]
public class RequesterTaxStepGrantsTests(PostgresFixture fixture)
{
    /// <summary>Seeded by <c>DevDataSeeder.SeedLocalUsersAsync</c> as the local Requester.</summary>
    private const string RequesterEmail = "solicitante@ulbra.local";

    [Fact]
    public async Task Seeded_requester_can_list_ledger_accounts()
    {
        var client = await fixture.Api.CreateSeededUserClientAsync(RequesterEmail, Roles.Requester);

        var res = await client.GetAsync("/api/ledger-accounts");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Seeded_requester_can_preview_withholding_rules()
    {
        var client = await fixture.Api.CreateSeededUserClientAsync(RequesterEmail, Roles.Requester);

        var res = await client.GetAsync("/api/withholding-rules/preview?regime=LucroReal");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
