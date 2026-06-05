namespace Ulbra.One.Common.Application.Pagination;

/// <summary>
/// Envelope for a single page of a paginated list.
/// </summary>
public sealed record PaginatedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int Total,
    int TotalPages)
{
    /// <summary>Projects the items to another type, preserving the page metadata.</summary>
    public PaginatedResponse<TOut> Map<TOut>(Func<T, TOut> selector) =>
        new(Items.Select(selector).ToList(), Page, PageSize, Total, TotalPages);
}
