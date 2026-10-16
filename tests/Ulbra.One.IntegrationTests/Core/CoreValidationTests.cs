using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Domain.Errors;
using Xunit;

namespace Ulbra.One.IntegrationTests.Core;

/// <summary>
/// The Core aggregates take their fields through plain setters — <c>Company.Update</c> assigns
/// <c>LegalName</c> and <c>Cnpj</c> without looking at them. Nothing above them checked either, so the API
/// happily answered 201 to a company with an empty razão social and a CNPJ of "nao-e-um-cnpj", and 200 to
/// an update that blanked a real one.
/// <para>
/// These are the requests that used to succeed. Each asserts a 400 with per-field errors, so the fix
/// cannot be undone by removing the filter without a test going red.
/// </para>
/// </summary>
[Collection("Postgres")]
public class CoreValidationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Company_cannot_be_created_without_a_legal_name_or_with_an_invalid_cnpj()
    {
        var client = await fixture.Api.CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "", cnpj = "nao-e-um-cnpj" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await FieldErrorsAsync(response);
        Assert.Contains("LegalName", errors);
        Assert.Contains("Cnpj", errors);
    }

    [Fact]
    public async Task Company_cannot_be_blanked_by_an_update()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var created = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Empresa Válida", cnpj = "99112233000224" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var response = await client.PutAsJsonAsync($"/api/companies/{id}",
            new { legalName = "", cnpj = "@@@" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("LegalName", await FieldErrorsAsync(response));
    }

    [Fact]
    public async Task Cost_center_cannot_be_created_blank_or_with_a_negative_auto_approval_limit()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var company = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Empresa do Centro de Custo", cnpj = "99112233000305" });
        company.EnsureSuccessStatusCode();
        var companyId = (await company.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var response = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "", name = "", autoApprovalLimit = -500m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await FieldErrorsAsync(response);
        Assert.Contains("Code", errors);
        Assert.Contains("Name", errors);
        // The limit is what auto-approves payables without human review, so a negative value is not cosmetic.
        Assert.Contains("AutoApprovalLimit", errors);
    }

    [Fact]
    public async Task Unit_cannot_be_created_without_a_name_or_acronym()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var company = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Empresa da Unidade", cnpj = "99112233000496" });
        company.EnsureSuccessStatusCode();
        var companyId = (await company.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var response = await client.PostAsJsonAsync("/api/units",
            new { companyId, name = "", acronym = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await FieldErrorsAsync(response);
        Assert.Contains("Name", errors);
        Assert.Contains("Acronym", errors);
    }

    [Fact]
    public async Task A_valid_company_is_still_accepted()
    {
        var client = await fixture.Api.CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Universidade Luterana do Brasil", cnpj = "99112233000143" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_duplicate_company_cnpj_is_a_conflict_not_a_crash()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        const string cnpj = "99112233000577";
        (await client.PostAsJsonAsync("/api/companies", new { legalName = "Primeira", cnpj }))
            .EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/companies", new { legalName = "Segunda", cnpj });

        // The unique index used to surface as a 500 — registering the same company twice is an ordinary
        // mistake, and the caller cannot tell a conflict from a broken server.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ErrorCodes.Company.CnpjDuplicate,
            body.RootElement.GetProperty(ErrorResults.ErrorCodeKey).GetString());
    }

    /// <summary>
    /// Validation failures go through <c>ErrorResults</c> like every other error, so they carry the same
    /// envelope — asserted here so the filter cannot regress to a bare ValidationProblem unnoticed.
    /// </summary>
    private static async Task<IReadOnlyCollection<string>> FieldErrorsAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.Equal(ErrorCodes.General.ValidationFailed, root.GetProperty(ErrorResults.ErrorCodeKey).GetString());
        Assert.Equal(32, root.GetProperty(ErrorResults.TraceIdKey).GetString()!.Length);

        return root.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
    }
}
