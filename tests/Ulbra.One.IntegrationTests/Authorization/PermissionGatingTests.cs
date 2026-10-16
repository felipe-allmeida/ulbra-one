using System.Net;
using Ulbra.One.Common.Application.Authentication; // Permissions
using Ulbra.One.Identity.Domain; // Roles

namespace Ulbra.One.IntegrationTests.Authorization;

/// <summary>
/// End-to-end claim-based authorization: a role without an endpoint's permission gets 403; Admin
/// bypasses. Mint the token via <see cref="ApiFactory.CreateClientWith"/> with the desired
/// role/permissions — the `perm:&lt;x&gt;` policy decides before the handler, so it doesn't depend
/// on the resource existing.
/// </summary>
[Collection("Postgres")]
public class PermissionGatingTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Finance_without_payables_approve_gets_403_on_approve()
    {
        var factory = fixture.Api;
        // Finance has payables read/write/pay, but NOT approve (segregation of duties).
        var client = factory.CreateClientWith(Roles.Finance, Permissions.PayablesRead, Permissions.PayablesWrite, Permissions.PayablesPay);

        var res = await client.PostAsync($"/api/payables/{Guid.NewGuid()}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Accounting_without_payables_read_gets_403_on_list_payables()
    {
        var factory = fixture.Api;
        // Accounting has no payables:read → cannot list accounts payable.
        var client = factory.CreateClientWith(Roles.Accounting, Permissions.AccountingRead, Permissions.AccountingWrite, Permissions.AccountingPost);

        var res = await client.GetAsync("/api/payables");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Accounting_without_payables_read_gets_403_on_payment_queue_summary()
    {
        var factory = fixture.Api;
        // Same segregation as the list: without payables:read, the payment-queue summary is 403.
        var client = factory.CreateClientWith(Roles.Accounting, Permissions.AccountingRead, Permissions.AccountingWrite, Permissions.AccountingPost);

        var res = await client.GetAsync("/api/payables/payment-queue-summary");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Accounting_without_payables_read_gets_403_on_payments_history()
    {
        var factory = fixture.Api;
        // The settlement history also requires payables:read.
        var client = factory.CreateClientWith(Roles.Accounting, Permissions.AccountingRead, Permissions.AccountingWrite, Permissions.AccountingPost);

        var res = await client.GetAsync("/api/payables/payments");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Admin_bypasses_the_permission_gate()
    {
        var factory = fixture.Api;
        // Admin needs no permission claims — the policy bypass passes the gate.
        var client = factory.CreateClientWith(Roles.Admin);

        var res = await client.PostAsync($"/api/payables/{Guid.NewGuid()}/approve", null);

        // Passed authorization (not 403/401); the non-existent resource resolves later in the handler.
        Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
