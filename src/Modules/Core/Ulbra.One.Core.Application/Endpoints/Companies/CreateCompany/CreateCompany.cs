using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.Companies.CreateCompany;

public sealed class CreateCompany : IEndpoint
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
        builder.MapPost("", Handle)
            .WithName("CreateCompany")
            .WithTags("Core")
            .WithSummary("Creates a company.")
            .WithDescription("Registers a company — the root of the org chart. Everything else in the "
                + "system hangs off one: units, cost centers, and every document that carries a companyId.")
            .RequirePermission(Permissions.CoreWrite)
            .WithRequestNormalizer<Request>()
            .WithValidation<Request>()
            .Produces<CompanyResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Handle(
        Request req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        // The unique index is filtered on deleted_at, so a soft-deleted company does not block reuse.
        // Without this check the constraint surfaces as a 500 — a plain duplicate is a conflict, not a bug.
        if (await uow.ReadSet<Company>().AnyAsync(c => c.Cnpj == req.Cnpj, ct))
            return ErrorResults.InvalidState("A company with this CNPJ already exists.", ErrorCodes.Company.CnpjDuplicate);

        var company = new Company(req.LegalName, req.Cnpj);
        await uow.AddAsync(company, ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/companies/{new CompanyId(company.Id)}", CompanyResponse.From(company));
    }
}
