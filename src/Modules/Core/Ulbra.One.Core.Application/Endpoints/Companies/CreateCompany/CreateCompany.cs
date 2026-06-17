using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.Companies.CreateCompany;

public sealed class CreateCompany : IEndpoint
{
    public sealed record Request
    {
        [NormalizeText] public string LegalName { get; set; } = string.Empty;
        [NormalizeText] public string Cnpj { get; set; } = string.Empty;
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("", Handle)
            .WithName("CreateCompany")
            .WithTags("Core")
            .RequireAuthorization()
            .WithRequestNormalizer<Request>()
            .Produces<CompanyResponse>(StatusCodes.Status201Created);
    }

    private static async Task<IResult> Handle(
        Request req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var company = new Company(req.LegalName, req.Cnpj);
        await uow.AddAsync(company, ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/companies/{new CompanyId(company.Id)}", CompanyResponse.From(company));
    }
}
