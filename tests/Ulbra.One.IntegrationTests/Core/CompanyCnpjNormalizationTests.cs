using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Ulbra.One.IntegrationTests.Core;

// Exercised through the endpoints, not the validator: the defect is the handler persisting what the
// validator only normalized in passing, which a test of the validator cannot see.
[Collection("Postgres")]
public class CompanyCnpjNormalizationTests(PostgresFixture fixture)
{
    // Official RFB example of the alphanumeric CNPJ (Jul/2026).
    private const string AlphanumericCnpj = "12ABC34501DE35";
    private const string AlphanumericCnpjFormatted = "12.ABC.345/01DE-35";

    [Fact]
    public async Task Create_accepts_a_formatted_cnpj_and_stores_it_bare()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var bare = TestCnpj.New();

        var response = await client.PostAsJsonAsync("/api/companies", new
        {
            legalName = "Empresa Formatada",
            cnpj = TestCnpj.Format(bare),
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompanyPayload>();
        Assert.Equal(bare, body!.Cnpj);
    }

    [Fact]
    public async Task Update_accepts_a_formatted_cnpj_and_stores_it_bare()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var created = await CreateAsync(client, "Empresa Para Editar", TestCnpj.New());
        var newBare = TestCnpj.New();

        var response = await client.PutAsJsonAsync($"/api/companies/{created.Id}", new
        {
            legalName = "Empresa Editada",
            cnpj = TestCnpj.Format(newBare),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompanyPayload>();
        Assert.Equal(newBare, body!.Cnpj);
    }

    // The duplicate check compared the raw request against stored values, so the same company sent
    // formatted slipped past it and only failed at the database.
    [Fact]
    public async Task Duplicate_is_detected_even_when_the_formats_differ()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var bare = TestCnpj.New();
        await CreateAsync(client, "Empresa Original", bare);

        var response = await client.PostAsJsonAsync("/api/companies", new
        {
            legalName = "Empresa Duplicada",
            cnpj = TestCnpj.Format(bare),
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(AlphanumericCnpj)]
    [InlineData(AlphanumericCnpjFormatted)]
    public async Task Create_accepts_the_alphanumeric_cnpj(string cnpj)
    {
        var client = await fixture.Api.CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/companies", new
        {
            legalName = $"Empresa Alfanumérica {Guid.NewGuid():N}",
            cnpj,
        });

        // Either it was created now, or a sibling case created it first — both prove it validated.
        Assert.True(
            response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"Alphanumeric CNPJ was rejected with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    // The unit handler normalized with the digits-only helper, so an alphanumeric CNPJ reached the
    // column with its letters removed — a wrong value stored without any error.
    [Fact]
    public async Task Unit_keeps_the_letters_of_an_alphanumeric_cnpj()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var company = await CreateAsync(client, $"Empresa Da Filial {Guid.NewGuid():N}", TestCnpj.New());

        var response = await client.PostAsJsonAsync("/api/units", new
        {
            companyId = company.Id,
            name = "Filial Alfanumérica",
            acronym = "ALF",
            cnpj = AlphanumericCnpjFormatted,
            isMatriz = false,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UnitPayload>();
        Assert.Equal(AlphanumericCnpj, body!.Cnpj);
    }

    private static async Task<CompanyPayload> CreateAsync(HttpClient client, string legalName, string cnpj)
    {
        var response = await client.PostAsJsonAsync("/api/companies", new { legalName, cnpj });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CompanyPayload>())!;
    }

    private sealed record CompanyPayload(string Id, string LegalName, string Cnpj, bool Active);

    private sealed record UnitPayload(string Id, string CompanyId, string Name, string Acronym, string? Cnpj);
}
