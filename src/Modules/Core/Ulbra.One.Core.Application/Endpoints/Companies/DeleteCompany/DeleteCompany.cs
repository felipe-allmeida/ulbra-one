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
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.Companies.DeleteCompany;

public sealed class DeleteCompany : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapDelete("/{id}", Handle)
            .WithName("DeleteCompany")
            .WithTags("Core")
            .WithSummary("Deletes a company.")
            .WithDescription("Soft-deletes the company (admin only): the row is kept and stamped, so it drops from "
                + "every list and the company selector while documents that reference its id still resolve. To take "
                + "a company out of use while keeping it visible, deactivate it instead.")
            .RequireAdmin()
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        CompanyId id,
        IUnitOfWork uow,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var company = await uow.Set<Company>().FirstOrDefaultAsync(e => e.Id == id.Value, ct);
        if (company is null)
            return Result.Failure(Error.NotFound(ErrorCodes.Company.NotFound, "Company not found.")).ToOk();
        company.MarkDeleted(timeProvider.GetUtcNow());
        await uow.SaveChangesAsync(ct);
        return Result.Success().ToOk();
    }
}
