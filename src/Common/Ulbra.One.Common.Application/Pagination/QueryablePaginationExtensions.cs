using Microsoft.EntityFrameworkCore;

namespace Ulbra.One.Common.Application.Pagination;

public static class QueryablePaginationExtensions
{
    /// <summary>
    /// Counts the total rows, applies Skip/Take from <paramref name="pagination"/>, and
    /// returns a <see cref="PaginatedResponse{T}"/> with computed TotalPages.
    /// Apply ordering and any filters before calling.
    /// </summary>
    public static async Task<PaginatedResponse<T>> ToPaginatedAsync<T>(
        this IQueryable<T> query,
        PaginationQueryParameters pagination,
        CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip(pagination.Skip).Take(pagination.Take).ToListAsync(ct);
        var pageSize = pagination.NormalizedPageSize;
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        return new PaginatedResponse<T>(items, pagination.NormalizedPage, pageSize, total, totalPages);
    }
}
