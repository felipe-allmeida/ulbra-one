using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Eventing;

[Collection("PostgresRabbitMq")]
public class PayablePendingApprovalTests(PostgresFixture pgFixture, RabbitMqFixture rabbitFixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);

    private static async Task<Seed> SeedAsync(HttpClient client, decimal autoApprovalLimit = 0)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "ULBRA Eventing Teste", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "EVT1", name = "Eventing CC", annualBudget = 1_000_000m, autoApprovalLimit, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        // Vendor document uniqueness is scoped to (CompanyId, Document) — same CNPJ can be reused across companies.
        var vendorRes = await client.PostAsJsonAsync("/api/vendors",
            new
            {
                companyId,
                personType = 1,
                name = "Fornecedor Eventing Ltda",
                tradeName = "Forn Eventing",
                document = "11.222.333/0001-81",
                email = "eventing@test.com",
                phone = "5133334444",
                address = (object?)null,
                paymentTerms = "30 dias",
                bankInfo = (string?)null,
            });
        vendorRes.EnsureSuccessStatusCode();
        var vendorId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        return new Seed(companyId, costCenterId, vendorId);
    }

    private ApiFactory CreateFactory() =>
        new ApiFactory(
            pgFixture.ConnectionString,
            rabbitFixture.Host,
            rabbitFixture.Port,
            rabbitFixture.Username,
            rabbitFixture.Password);

    /// <summary>
    /// E2E: payable ABOVE limit → PendingApproval → outbox publishes to broker → consumer creates Notification.
    /// Polls GET /api/notifications with a bounded retry (up to 15 s) to handle async delivery.
    /// </summary>
    [Fact]
    public async Task PendingApproval_Payable_Triggers_Notification_Via_Broker()
    {
        await using var factory = CreateFactory();
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client, autoApprovalLimit: 5_000m);

        // Create a payable ABOVE the auto-approval limit → stays PendingApproval
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "E2E eventing test payable",
            installments = new[] { new { dueDate = "2026-08-15", amount = 10_000m } },
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var payableBody = await ReadJson(createRes);
        Assert.Equal("PendingApproval", payableBody.GetProperty("status").GetString());

        // Extract the raw payable Guid from the prefixed ID string (e.g. "payable_<guid>")
        var payableIdStr = payableBody.GetProperty("id").GetString()!;
        var payableGuid = Guid.Parse(payableIdStr.Split('_')[^1]);

        // Poll GET /api/notifications until a PayablePendingApproval notification for this payable appears.
        // Bounded retry: up to ~60 s total (120 attempts × 500 ms) to handle outbox delivery + broker round-trip.
        JsonElement? foundNotification = null;
        for (var attempt = 0; attempt < 120; attempt++)
        {
            await Task.Delay(500);
            var notifRes = await client.GetAsync("/api/notifications?pageSize=100");
            Assert.Equal(HttpStatusCode.OK, notifRes.StatusCode);
            var notifBody = await ReadJson(notifRes);
            var items = notifBody.GetProperty("items").EnumerateArray();
            foundNotification = items
                .Cast<JsonElement?>()
                .FirstOrDefault(n =>
                    n!.Value.GetProperty("type").GetString() == "PayablePendingApproval" &&
                    n!.Value.TryGetProperty("referenceId", out var refEl) &&
                    refEl.ValueKind != JsonValueKind.Null &&
                    refEl.GetGuid() == payableGuid);
            if (foundNotification.HasValue) break;
        }

        Assert.True(foundNotification.HasValue,
            $"Expected a PayablePendingApproval notification for payable {payableGuid} but none appeared within 60 s.");

        var n = foundNotification!.Value;
        Assert.Equal("PayablePendingApproval", n.GetProperty("type").GetString());
        Assert.Equal("Conta pendente de aprovação", n.GetProperty("title").GetString());
        Assert.Equal(payableGuid, n.GetProperty("referenceId").GetGuid());
        Assert.Contains("Fornecedor Eventing Ltda", n.GetProperty("message").GetString());
    }

    /// <summary>
    /// Atomicity: payable WITHIN the auto-approval limit → auto-approved → NO notification is published.
    /// </summary>
    [Fact]
    public async Task AutoApproved_Payable_Does_Not_Trigger_Notification()
    {
        await using var factory = CreateFactory();
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client, autoApprovalLimit: 20_000m);

        // Create a payable WITHIN the auto-approval limit → auto-approved
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Auto-approved payable — should not notify",
            installments = new[] { new { dueDate = "2026-08-15", amount = 1_000m } },
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var payableBody = await ReadJson(createRes);
        Assert.Equal("Approved", payableBody.GetProperty("status").GetString());

        var payableIdStr = payableBody.GetProperty("id").GetString()!;
        var payableGuid = Guid.Parse(payableIdStr.Split('_')[^1]);

        // Wait long enough that any spurious message would have been delivered and persisted.
        // Must comfortably exceed QueryDelay (1 s) + broker round-trip + consumer processing.
        // 8 s gives ample margin so a false "no notification" result cannot occur vacuously.
        await Task.Delay(8_000);

        var notifRes = await client.GetAsync("/api/notifications?pageSize=100");
        Assert.Equal(HttpStatusCode.OK, notifRes.StatusCode);
        var notifBody = await ReadJson(notifRes);
        var items = notifBody.GetProperty("items").EnumerateArray();
        var spurious = items.Cast<JsonElement?>().FirstOrDefault(n =>
            n!.Value.GetProperty("type").GetString() == "PayablePendingApproval" &&
            n!.Value.TryGetProperty("referenceId", out var refEl) &&
            refEl.ValueKind != JsonValueKind.Null &&
            refEl.GetGuid() == payableGuid);

        Assert.False(spurious.HasValue,
            $"Expected NO notification for auto-approved payable {payableGuid}, but one was found.");
    }

    /// <summary>
    /// Verifies that a single publish of a PendingApproval payable results in exactly one
    /// Notification being persisted. This integration test covers the happy-path delivery count;
    /// the consumer's AnyAsync guard (preventing duplicates on redelivery) is covered separately
    /// by the consumer unit tests.
    /// </summary>
    [Fact]
    public async Task Consumer_Is_Idempotent_Only_One_Notification_Per_Payable()
    {
        await using var factory = CreateFactory();
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client, autoApprovalLimit: 500m);

        // Create a pending payable
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Idempotency test payable",
            installments = new[] { new { dueDate = "2026-08-15", amount = 1_000m } },
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var payableBody = await ReadJson(createRes);
        Assert.Equal("PendingApproval", payableBody.GetProperty("status").GetString());

        var payableIdStr = payableBody.GetProperty("id").GetString()!;
        var payableGuid = Guid.Parse(payableIdStr.Split('_')[^1]);

        // Wait for the initial notification to appear (up to 60 s).
        bool appeared = false;
        for (var attempt = 0; attempt < 120; attempt++)
        {
            await Task.Delay(500);
            var notifRes = await client.GetAsync("/api/notifications?pageSize=100");
            var notifBody = await ReadJson(notifRes);
            var items = notifBody.GetProperty("items").EnumerateArray();
            appeared = items.Any(n =>
                n.GetProperty("type").GetString() == "PayablePendingApproval" &&
                n.TryGetProperty("referenceId", out var refEl) &&
                refEl.ValueKind != JsonValueKind.Null &&
                refEl.GetGuid() == payableGuid);
            if (appeared) break;
        }

        Assert.True(appeared, $"Notification for payable {payableGuid} did not appear within 60 s.");

        // Wait a bit more for any potential duplicate delivery to settle.
        await Task.Delay(3_000);

        // Assert exactly ONE notification exists for this payable.
        var finalRes = await client.GetAsync("/api/notifications?pageSize=500");
        Assert.Equal(HttpStatusCode.OK, finalRes.StatusCode);
        var finalBody = await ReadJson(finalRes);
        var count = finalBody.GetProperty("items").EnumerateArray()
            .Count(n =>
                n.GetProperty("type").GetString() == "PayablePendingApproval" &&
                n.TryGetProperty("referenceId", out var refEl) &&
                refEl.ValueKind != JsonValueKind.Null &&
                refEl.GetGuid() == payableGuid);

        Assert.Equal(1, count);
    }
}
