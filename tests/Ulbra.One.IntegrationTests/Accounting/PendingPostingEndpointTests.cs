using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.IntegrationTests.Accounting;

/// <summary>
/// Integration tests for GET /api/accounting/pending-postings.
/// Seeds <see cref="PendingAccountingPosting"/> rows directly via DI,
/// verifying that open rows appear and resolved rows are excluded.
/// </summary>
[Collection("Postgres")]
public class PendingPostingEndpointTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string> SeedCompanyAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var res = await client.PostAsJsonAsync("/api/companies", new { legalName = "Empresa Pendentes Teste", cnpj });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/accounting/pending-postings");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task OpenRow_Appears_ResolvedRow_DoesNot()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        // Create a company and resolve its prefixed id to a raw Guid.
        var prefixedId = await SeedCompanyAsync(client);
        var companyId = Ulbra.One.Common.Application.Ids.CompanyId.Parse(prefixedId, null).Value;

        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = DateTimeOffset.UtcNow;

        // Open (unresolved) posting.
        var open = new PendingAccountingPosting(
            companyId, $"payable-incurral:{Guid.NewGuid()}", PostingKind.Incurral,
            150m, "Código de despesa não mapeado", "9.99.99.99.99");
        open.Stamp(now.AddMinutes(-2));

        // Resolved posting — must NOT appear in results.
        var resolved = new PendingAccountingPosting(
            companyId, $"payable-incurral:{Guid.NewGuid()}", PostingKind.Settlement,
            200m, "Conta bancária não mapeada", "1.01.01.02.01");
        resolved.Stamp(now.AddMinutes(-5));
        resolved.MarkResolved(now);

        await uow.AddAsync(open, CancellationToken.None);
        await uow.AddAsync(resolved, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        var res = await client.GetAsync($"/api/accounting/pending-postings?companyId={prefixedId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await ReadJson(res);
        var items = body.GetProperty("items");

        // Only the open row must appear.
        Assert.Equal(1, items.GetArrayLength());
        var item = items[0];
        Assert.Equal("Incurral", item.GetProperty("kind").GetString());
        Assert.Equal(150m, item.GetProperty("amount").GetDecimal());
        Assert.Equal("Código de despesa não mapeado", item.GetProperty("reason").GetString());
        Assert.Equal("9.99.99.99.99", item.GetProperty("intendedCodes").GetString());

        // The id must carry the pendpost_ prefix.
        var id = item.GetProperty("id").GetString()!;
        Assert.StartsWith("pendpost_", id);
    }

    [Fact]
    public async Task EmptyCompany_Returns_EmptyPage()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var prefixedId = await SeedCompanyAsync(client);

        var res = await client.GetAsync($"/api/accounting/pending-postings?companyId={prefixedId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await ReadJson(res);
        Assert.Equal(0, body.GetProperty("total").GetInt32());
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task NewestFirst_Ordering()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var prefixedId = await SeedCompanyAsync(client);
        var companyId = Ulbra.One.Common.Application.Ids.CompanyId.Parse(prefixedId, null).Value;

        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var base_ = DateTimeOffset.UtcNow;

        var older = new PendingAccountingPosting(companyId, $"k:{Guid.NewGuid()}", PostingKind.Incurral, 10m, "old", "x");
        older.Stamp(base_.AddHours(-1));
        var newer = new PendingAccountingPosting(companyId, $"k:{Guid.NewGuid()}", PostingKind.Settlement, 20m, "new", "y");
        newer.Stamp(base_);

        await uow.AddAsync(older, CancellationToken.None);
        await uow.AddAsync(newer, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        var res = await client.GetAsync($"/api/accounting/pending-postings?companyId={prefixedId}");
        res.EnsureSuccessStatusCode();
        var items = (await ReadJson(res)).GetProperty("items");

        Assert.Equal(2, items.GetArrayLength());
        // Newest first: Settlement (20) should come before Incurral (10).
        Assert.Equal(20m, items[0].GetProperty("amount").GetDecimal());
        Assert.Equal(10m, items[1].GetProperty("amount").GetDecimal());
    }
}
