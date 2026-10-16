using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Registry.Application.Contracts;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Application.Endpoints.Customers.ListCustomers;

/// <summary>Query-string filters for the customer browse list. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListCustomersRequest : PaginationQueryParameters
{
    [SwaggerSchema("Company that owns the customers. Omit to list across every company.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Free-text search over the customer name and document. The name match ignores case; "
        + "the document is matched the way it is stored, so a formatted CNPJ/CPF works.")]
    [SwaggerExample("Aurora")]
    public string? Search { get; set; }
}

public sealed class ListCustomers : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListCustomers")
            .WithTags("Registry")
            .WithSummary("Lists customers.")
            .WithDescription("Paginated browse list of customers, ordered by name. Both filters are "
                + "optional and combine with AND.")
            .RequirePermission(Permissions.RegistryRead)
            .Produces<PaginatedResponse<CustomerResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        [AsParameters] ListCustomersRequest req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var query = uow.ReadSet<CustomerEntity>();
        if (req.CompanyId is { } cid) query = query.Where(c => c.CompanyId == cid.Value);
        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            // Name match ignores case (lower); the document is matched the way it is stored
            // (alphanumeric CNPJ / digits CPF).
            var term = req.Search.ToLower();
            var docTerm = DocumentValidator.NormalizeCnpj(req.Search);
            query = query.Where(c =>
                c.Name.ToLower().Contains(term)
                || (docTerm != "" && c.Document.Contains(docTerm)));
        }
        var page = await query.OrderBy(c => c.Name).ToPaginatedAsync(req, ct);
        return TypedResults.Ok(page.Map(CustomerResponse.From));
    }
}
