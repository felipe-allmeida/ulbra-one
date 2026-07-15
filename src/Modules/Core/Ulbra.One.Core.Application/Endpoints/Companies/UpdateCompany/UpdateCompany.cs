using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.Companies.UpdateCompany;

public sealed class UpdateCompany : IEndpoint
{
    public sealed record Request
    {
        [NormalizeText] public string LegalName { get; set; } = string.Empty;
        [NormalizeText] public string Cnpj { get; set; } = string.Empty;
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{id}", Handle)
            .WithName("UpdateCompany")
            .WithTags("Core")
            .RequirePermission(Permissions.CoreWrite)
            .WithRequestNormalizer<Request>()
            .Produces<CompanyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
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
