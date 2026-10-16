using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.Companies.UpdateCompany;

public sealed class UpdateCompany : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Legal name (razão social) of the company.")]
        [SwaggerExample("Universidade Luterana do Brasil")]
        [NormalizeText] public string LegalName { get; set; } = string.Empty;

        [SwaggerSchema("Company CNPJ. Accepted formatted or bare.")]
        [SwaggerExample("88.332.580/0001-90")]
        [NormalizeCnpj] public string Cnpj { get; set; } = string.Empty;
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{id}", Handle)
            .WithName("UpdateCompany")
            .WithTags("Core")
            .WithSummary("Updates a company.")
            .WithDescription("Replaces the company's legal name and CNPJ. A full replace, not a patch. "
                + "Units and cost centers already under it are untouched.")
            .RequirePermission(Permissions.CoreWrite)
            .WithRequestNormalizer<Request>()
            .WithValidation<Request>()
            .Produces<CompanyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> Handle(
        CompanyId id,
        Request req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var company = await uow.Set<Company>().FirstOrDefaultAsync(e => e.Id == id.Value, ct);
        if (company is null) return TypedResults.NotFound();
        company.Update(req.LegalName, req.Cnpj);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(CompanyResponse.From(company));
    }
}
