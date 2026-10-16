using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.Identity.Domain;
using Ulbra.One.IntegrationTests.Finance;
using Xunit;

namespace Ulbra.One.IntegrationTests.Budgeting;

/// <summary>GET /requisitions/budget-projection (AEL-876): aggregate multi-month projection of the whole queue
/// against the budget — orçado summed over all cost centers vs consumido + previsto (fila) per competency,
/// marking the months the queue would breach.</summary>
[Collection("Postgres")]
public class RequisitionBudgetProjectionTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static JsonElement MonthOf(JsonElement projection, string competency) =>
        projection.GetProperty("months").EnumerateArray().Single(m => m.GetProperty("competency").GetString() == competency);

    [Fact]
    public async Task Projects_Queue_Against_Ceiling_And_Flags_Breach_Month()
    {
        var admin = fixture.Api.CreateClientWith(Roles.Admin);

        var companyId = await FinanceSeed.CreateCompanyAsync(admin);
        var unitId = await FinanceSeed.CreateUnitAsync(admin, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(
            admin, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "Marketing", unitId);

        // Ceilings vary per month so exactly one competency breaches: 08 is tight (1.000), 07/09 are loose (5.000).
        foreach (var (competency, limit) in new[] { ("2026-07", 5_000m), ("2026-08", 1_000m), ("2026-09", 5_000m) })
            await Ok(await admin.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets", new
            {
                unitId,
                startCompetency = competency,
                endCompetency = competency,
                monthlyLimit = limit,
            }));

        var vendorId = await FinanceSeed.CreateVendorAsync(admin, "Fornecedor Teste");
        // Monthly total 6.000 over 3 months → 2.000/mês (previsto, pois fica em Triagem).
        var created = await Ok(await admin.PostAsJsonAsync("/api/requisitions", new
        {
            unitId,
            costCenterId,
            vendorId,
            type = "Purchase",
            description = "Assinatura anual",
            observation = (string?)null,
            estimateValue = 6_000m,
            estimateType = "Monthly",
            startCompetency = "2026-07",
            endCompetency = "2026-09",
            firstDueDate = "2026-07-10",
            paymentFrequency = (string?)null,
            paymentInstallmentsCount = (int?)null,
        }));
        var id = created.GetProperty("id").GetString()!;
        await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{id}/submit", new { }));

        var projection = await Ok(await admin.GetAsync(
            $"/api/requisitions/budget-projection?companyId={companyId}&from=2026-07&to=2026-10"));

        var jul = MonthOf(projection, "2026-07");
        Assert.Equal(5_000m, jul.GetProperty("orcado").GetDecimal());
        Assert.Equal(2_000m, jul.GetProperty("forecast").GetDecimal());
        Assert.Equal(0m, jul.GetProperty("consumed").GetDecimal());
        Assert.True(jul.GetProperty("budgeted").GetBoolean());

        var ago = MonthOf(projection, "2026-08");
        Assert.Equal(1_000m, ago.GetProperty("orcado").GetDecimal());
        Assert.Equal(2_000m, ago.GetProperty("forecast").GetDecimal());

        // Mês sem célula de orçamento → sem teto, fora dos breaches.
        var outubro = MonthOf(projection, "2026-10");
        Assert.Equal(0m, outubro.GetProperty("orcado").GetDecimal());
        Assert.False(outubro.GetProperty("budgeted").GetBoolean());

        var breaches = projection.GetProperty("breaches").EnumerateArray().Select(b => b.GetString()).ToList();
        Assert.Equal(["2026-08"], breaches);

        Assert.Equal(6_000m, projection.GetProperty("queueTotal").GetDecimal());
    }

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        var res = await fixture.Api.CreateClient().GetAsync("/api/requisitions/budget-projection?companyId=company_x");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
