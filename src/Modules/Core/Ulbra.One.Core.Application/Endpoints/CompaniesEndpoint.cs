using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Core.Infrastructure.Database;

namespace Ulbra.One.Core.Application.Endpoints;

public sealed class CompaniesEndpoint : IEndpoint
{
    public record CompanyRequest(string LegalName, string Cnpj);

    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/companies").WithTags("Core");

        group.MapGet("/", async (CoreContext db, CancellationToken ct) =>
            Results.Ok(await db.Companies.OrderBy(e => e.LegalName).ToListAsync(ct)))
            .WithName("ListCompanies");

        group.MapPost("/", async (CompanyRequest req, CoreContext db, CancellationToken ct) =>
        {
            var company = new Company(req.LegalName, req.Cnpj);
            db.Companies.Add(company);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/companies/{company.Id}", company);
        }).WithName("CreateCompany");

        group.MapPut("/{id:guid}", async (Guid id, CompanyRequest req, CoreContext db, CancellationToken ct) =>
        {
            var company = await db.Companies.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (company is null) return Results.NotFound();
            company.Update(req.LegalName, req.Cnpj);
            await db.SaveChangesAsync(ct);
            return Results.Ok(company);
        }).WithName("UpdateCompany");
    }
}
