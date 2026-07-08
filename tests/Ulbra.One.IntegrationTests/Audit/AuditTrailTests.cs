using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Audit.Domain;
using Ulbra.One.Audit.Domain.Entities;
using Ulbra.One.Audit.Infrastructure;
using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.IntegrationTests.Audit;

/// <summary>
/// Proves the AuditLogInterceptor + audit_log table are wired into the business contexts and that the
/// audit row is written synchronously within the same request (via a separate AuditContext write in
/// SavedChanges, right after the business commit — separate-write model, not same-transaction).
/// </summary>
[Collection("Postgres")]
public class AuditTrailTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static string UniqueCode(string prefix) => $"{prefix}.{Random.Shared.Next(100_000, 999_999)}";

    // The API returns the prefixed id ("ledgacct_<guid:N>"); the audit_log.primary_key stores the
    // raw Guid in its default string form. Bridge the two so the trail can be looked up by API id.
    private static string PrimaryKeyOf(string apiId) => LedgerAccountId.Parse(apiId, null).Value.ToString();

    private static List<AuditLog> LogsFor(ApiFactory factory, string entityType, string primaryKey)
    {
        using var scope = factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AuditContext>();
        return ctx.AuditLogs
            .Where(l => l.EntityType == entityType && l.PrimaryKey == primaryKey)
            .OrderBy(l => l.OccurredAt)
            .ToList();
    }

    [Fact]
    public async Task Create_LedgerAccount_WritesAuditRow()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var code = UniqueCode("AUD");
        var res = await client.PostAsJsonAsync("/api/ledger-accounts",
            new { code, name = "Conta auditada", accountGroup = "Asset", kind = "Synthetic" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var id = (await ReadJson(res)).GetProperty("id").GetString()!;

        // The audit row is written synchronously in SavedChanges (separate AuditContext write after the
        // business commit), so it is already present by the time the 201 response is returned.
        var logs = LogsFor(factory, "LedgerAccount", PrimaryKeyOf(id));
        var create = Assert.Single(logs);

        Assert.Equal(AuditAction.Create, create.Action);
        Assert.Equal("LedgerAccount", create.EntityType);
        Assert.Equal("accounting", create.Schema);
        Assert.Equal("ledger_accounts", create.TableName);
        Assert.Null(create.OldValues);
        Assert.NotNull(create.NewValues);
        Assert.Contains(code, create.NewValues!);
        Assert.False(string.IsNullOrWhiteSpace(create.ActorUserId));
        Assert.NotEqual("system", create.ActorUserId);
    }

    [Fact]
    public async Task Update_LedgerAccount_WritesUpdateRow_WithAffectedColumns_ExcludingTimestamps()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var code = UniqueCode("AUD");
        var createRes = await client.PostAsJsonAsync("/api/ledger-accounts",
            new { code, name = "Antes", accountGroup = "Asset", kind = "Synthetic" });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var id = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var updateRes = await client.PutAsJsonAsync($"/api/ledger-accounts/{id}",
            new { name = "Depois", accountGroup = "Asset", kind = "Synthetic", normalBalance = "Debit", parentId = (string?)null, isActive = true });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        var logs = LogsFor(factory, "LedgerAccount", PrimaryKeyOf(id));
        var update = Assert.Single(logs, l => l.Action == AuditAction.Update);

        Assert.NotNull(update.AffectedColumns);
        Assert.Contains("Name", update.AffectedColumns!);
        Assert.DoesNotContain("UpdatedAt", update.AffectedColumns!);
        Assert.DoesNotContain("CreatedAt", update.AffectedColumns!);
    }

    [Fact]
    public async Task ListAuditLogs_ReturnsTrail_ForEntity_NewestFirst()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var code = UniqueCode("AUD");
        var createRes = await client.PostAsJsonAsync("/api/ledger-accounts",
            new { code, name = "Antes", accountGroup = "Asset", kind = "Synthetic" });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var id = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var updateRes = await client.PutAsJsonAsync($"/api/ledger-accounts/{id}",
            new { name = "Depois", accountGroup = "Asset", kind = "Synthetic", normalBalance = "Debit", parentId = (string?)null, isActive = true });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        var entityId = PrimaryKeyOf(id);
        var listRes = await client.GetAsync($"/api/audit-logs?entityType=LedgerAccount&entityId={entityId}");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);

        var body = await ReadJson(listRes);
        var items = body.GetProperty("items");
        Assert.True(body.GetProperty("total").GetInt32() >= 2);
        Assert.True(items.GetArrayLength() >= 2);

        // Newest-first: the Update must precede the Create in the response.
        Assert.Equal("Update", items[0].GetProperty("action").GetString());
        Assert.Equal(entityId, items[0].GetProperty("primaryKey").GetString());
        Assert.Contains(items.EnumerateArray(),
            x => x.GetProperty("action").GetString() == "Create");
    }
}
