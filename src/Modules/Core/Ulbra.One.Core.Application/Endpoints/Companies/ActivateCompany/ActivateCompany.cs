using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.Companies.ActivateCompany;

public sealed class ActivateCompany : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id}/activate", Handle)
            .WithName("ActivateCompany")
            .WithTags("Core")
            .WithSummary("Reactivates a company.")
            .WithDescription("Reverses a deactivation, marking the company active again. Idempotent.")
            .RequirePermission(Permissions.CoreWrite)
            .Produces<CompanyResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        CompanyId id,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var company = await uow.Set<Company>().FirstOrDefaultAsync(e => e.Id == id.Value, ct);
        if (company is null)
            return Result.Failure<CompanyResponse>(Error.NotFound(ErrorCodes.Company.NotFound, "Company not found.")).ToOk();
        company.Activate();
        await uow.SaveChangesAsync(ct);
        return Result.Success(CompanyResponse.From(company)).ToOk();
    }
}
