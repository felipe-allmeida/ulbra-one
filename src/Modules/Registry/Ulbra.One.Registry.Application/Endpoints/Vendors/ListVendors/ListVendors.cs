using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Registry.Application.Contracts;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Application.Endpoints.Vendors.ListVendors;

/// <summary>Query-string filters for the vendor browse list. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListVendorsRequest : PaginationQueryParameters
{
    [SwaggerSchema("Free-text search over the vendor name and document. The name match is "
        + "case-sensitive; the document is matched the way it is stored, so a formatted CNPJ/CPF works.")]
    [SwaggerExample("Farroupilha")]
    public string? Search { get; set; }
}

public sealed class ListVendors : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListVendors")
            .WithTags("Registry")
            .WithSummary("Lists vendors.")
            .WithDescription("Paginated browse list of vendors, ordered by name. Vendors are global to the "
                + "organization — available to every company — so unlike customers and employees this list "
                + "takes no company filter.")
            .RequirePermission(Permissions.RegistryRead)
            .Produces<PaginatedResponse<VendorResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        [AsParameters] ListVendorsRequest req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        // Vendors are global to the organization (available to every company): the list is not scoped by
        // company, only by the optional free-text search.
        var query = uow.ReadSet<VendorEntity>();
        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            // Match the document the same way it is stored (alphanumeric CNPJ / digits CPF).
            var search = req.Search;
            var docTerm = DocumentValidator.NormalizeCnpj(search);
            query = query.Where(v => v.Name.Contains(search) || (docTerm != "" && v.Document.Contains(docTerm)));
        }
        var page = await query.OrderBy(v => v.Name).ToPaginatedAsync(req, ct);
        return TypedResults.Ok(page.Map(VendorResponse.From));
    }
}
