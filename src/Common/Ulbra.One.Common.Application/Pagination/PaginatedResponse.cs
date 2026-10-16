using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Common.Application.Pagination;

/// <summary>
/// Envelope for a single page of a paginated list.
/// </summary>
[SwaggerSchema("One page of a list, with the metadata needed to walk the rest.")]
public sealed record PaginatedResponse<T>(
    [property: SwaggerSchema("The rows on this page. Empty when the page is past the end — that is not "
        + "an error, and Total still reports the real size of the set.")]
    IReadOnlyList<T> Items,
    [property: SwaggerSchema("Which page this is, 1-based.")]
    [property: SwaggerExample("1")]
    int Page,
    [property: SwaggerSchema("How many rows a page holds. It echoes the requested size after clamping, "
        + "so it can be smaller than what was asked for.")]
    [property: SwaggerExample("20")]
    int PageSize,
    [property: SwaggerSchema("How many rows the whole filtered set has, across every page.")]
    [property: SwaggerExample("184")]
    int Total,
    [property: SwaggerSchema("How many pages the set spans at this page size.")]
    [property: SwaggerExample("10")]
    int TotalPages)
{
    /// <summary>Projects the items to another type, preserving the page metadata.</summary>
    public PaginatedResponse<TOut> Map<TOut>(Func<T, TOut> selector) =>
        new(Items.Select(selector).ToList(), Page, PageSize, Total, TotalPages);
}
