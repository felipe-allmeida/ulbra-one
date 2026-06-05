namespace Ulbra.One.Common.Application.Pagination;

/// <summary>
/// Query-string pagination parameters for high-cardinality browse lists.
/// <see cref="Page"/> is 1-based; <see cref="PageSize"/> is clamped to [1, 100].
/// Both are optional in the query string and fall back to their defaults.
/// </summary>
public sealed class PaginationQueryParameters
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    /// <summary>Raw page from the query string (optional). Use <see cref="Page"/> for the clamped value.</summary>
    public int? Page { get; set; }

    /// <summary>Raw page size from the query string (optional). Use <see cref="PageSize"/> for the clamped value.</summary>
    public int? PageSize { get; set; }

    public int NormalizedPage => Page is { } p && p >= 1 ? p : 1;

    public int NormalizedPageSize => PageSize is { } s
        ? (s < 1 ? 1 : (s > MaxPageSize ? MaxPageSize : s))
        : DefaultPageSize;

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;

    public int Take => NormalizedPageSize;
}
