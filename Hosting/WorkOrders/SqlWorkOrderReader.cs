using Contracts.Common;
using Contracts.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace BlazorPdfApp.WorkOrders;

public sealed class SqlWorkOrderReader : IWorkOrderReader
{
    private readonly WorkOrderDbContext _dbContext;

    public SqlWorkOrderReader(WorkOrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PageResult<WorkOrderDto>> SearchAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        if (request.PageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.PageSize), "Page size must be greater than zero.");
        }

        var page = request.Page > 0 ? request.Page : 1;
        var query = _dbContext.WorkOrders.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search!;
            query = query.Where(order => order.Number.Contains(search) || order.PartNo.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(order => order.Status == request.Status);
        }

        if (!string.IsNullOrWhiteSpace(request.Line))
        {
            query = query.Where(order => order.Line == request.Line);
        }

        if (!string.IsNullOrWhiteSpace(request.PartNo))
        {
            query = query.Where(order => order.PartNo == request.PartNo);
        }

        if (request.FromUtc is not null)
        {
            var from = request.FromUtc.Value;
            query = query.Where(order => order.CreatedUtc >= from);
        }

        if (request.ToUtc is not null)
        {
            var to = request.ToUtc.Value;
            query = query.Where(order => order.CreatedUtc <= to);
        }

        query = query.OrderByDescending(order => order.CreatedUtc);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var skip = (page - 1) * request.PageSize;

        var items = await query
            .Skip(skip)
            .Take(request.PageSize)
            .Select(order => new WorkOrderDto(
                order.Id,
                order.Number,
                order.Status,
                order.Line,
                order.PartNo,
                order.CreatedUtc,
                order.DueUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PageResult<WorkOrderDto>(items, page, request.PageSize, totalCount);
    }

    public async Task<WorkOrderDto?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));
        }

        var entity = await _dbContext.WorkOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entity is null
            ? null
            : new WorkOrderDto(entity.Id, entity.Number, entity.Status, entity.Line, entity.PartNo, entity.CreatedUtc, entity.DueUtc);
    }
}
