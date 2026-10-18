using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Common.Application.Pagination;

/// <summary>
/// Query-string pagination parameters for high-cardinality browse lists.
/// <see cref="Page"/> is 1-based; <see cref="PageSize"/> is clamped to [1, 100].
/// Both are optional in the query string and fall back to their defaults.
/// <para>
/// A list endpoint's own filter object inherits from this, so the handler takes ONE
/// <c>[AsParameters]</c> request that carries filters and paging together (<c>[AsParameters]</c> binds
/// inherited properties but does not nest, so inheritance is what merges the two). Endpoints with no
/// filters of their own bind this type directly. It is a record so the request objects deriving from it
/// stay records, like every other endpoint DTO — a record cannot inherit from a class.
/// </para>
/// </summary>
public record PaginationQueryParameters
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    /// <summary>Raw page from the query string (optional). Use <see cref="NormalizedPage"/> for the clamped value.</summary>
    [SwaggerSchema("Page to return, 1-based. Omitted or below 1 falls back to the first page.")]
    [SwaggerExample("1")]
    public int? Page { get; set; }

    /// <summary>Raw page size from the query string (optional). Use <see cref="NormalizedPageSize"/> for the clamped value.</summary>
    [SwaggerSchema("Items per page. Clamped to [1, 100]; omitted defaults to 20.")]
    [SwaggerExample("20")]
    public int? PageSize { get; set; }

    public int NormalizedPage => Page is { } p && p >= 1 ? p : 1;

    public int NormalizedPageSize => PageSize is { } s
        ? (s < 1 ? 1 : (s > MaxPageSize ? MaxPageSize : s))
        : DefaultPageSize;

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;

    public int Take => NormalizedPageSize;
}
