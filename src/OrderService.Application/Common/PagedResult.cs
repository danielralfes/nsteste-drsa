namespace OrderService.Application.Common;

/// <summary>Envelope de resposta paginada: <c>{ items, page, pageSize, totalCount, totalPages }</c>.</summary>
public sealed record PagedResult<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
