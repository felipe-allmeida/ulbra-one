using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Core.Infrastructure.Database;

namespace Ulbra.One.Core.Application.Endpoints;

public sealed class CompaniesEndpoint : IEndpoint
{
    public record CompanyRequest(string LegalName, string Cnpj);

    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/companies").WithTags("Core").RequireAuthorization();

        group.MapGet("/", async (CoreContext db, CancellationToken ct) =>
        {
            var companies = await db.Companies.OrderBy(e => e.LegalName).ToListAsync(ct);
            return TypedResults.Ok(companies.Select(CompanyResponse.From).ToList());
        })
            .WithName("ListCompanies")
            .Produces<List<CompanyResponse>>(StatusCodes.Status200OK);

        group.MapPost("/", async (CompanyRequest req, CoreContext db, CancellationToken ct) =>
        {
            var company = new Company(req.LegalName, req.Cnpj);
            db.Companies.Add(company);
            await db.SaveChangesAsync(ct);
            return TypedResults.Created($"/api/companies/{company.Id}", CompanyResponse.From(company));
        })
            .WithName("CreateCompany")
            .Produces<CompanyResponse>(StatusCodes.Status201Created);

        group.MapPut("/{id:guid}", async Task<Results<Ok<CompanyResponse>, NotFound>> (Guid id, CompanyRequest req, CoreContext db, CancellationToken ct) =>
        {
            var company = await db.Companies.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (company is null) return TypedResults.NotFound();
            company.Update(req.LegalName, req.Cnpj);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(CompanyResponse.From(company));
        }).WithName("UpdateCompany");
    }
}
