using Microsoft.EntityFrameworkCore;

namespace Mrp.SharedKernel.Web;

/// <summary>One page of a list result.</summary>
/// <param name="Items">Rows of the page.</param>
/// <param name="Total">Total rows matching the filter.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>Paging helpers with bounded page size.</summary>
public static class Paging
{
    /// <summary>Largest page size a client may request.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Executes the query for one page, clamping the inputs to safe bounds.</summary>
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(page ?? 1, 1);
        var safeSize = Math.Clamp(pageSize ?? 50, 1, MaxPageSize);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((safePage - 1) * safeSize).Take(safeSize).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, total, safePage, safeSize);
    }
}
