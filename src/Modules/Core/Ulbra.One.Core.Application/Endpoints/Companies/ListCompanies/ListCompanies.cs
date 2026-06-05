using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.Companies.ListCompanies;

public sealed class ListCompanies : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListCompanies")
            .WithTags("Core")
            .RequireAuthorization()
            .Produces<List<CompanyResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        [FromKeyedServices("core")] IUnitOfWork uow,
        CancellationToken ct)
    {
        var companies = await uow.ReadSet<Company>().OrderBy(e => e.LegalName).ToListAsync(ct);
        return TypedResults.Ok(companies.Select(CompanyResponse.From).ToList());
    }
}
