using Contracts.Common;

namespace Contracts.WorkOrders;

public interface IWorkOrderReader
{
    Task<PageResult<WorkOrderDto>> SearchAsync(PageRequest request, CancellationToken cancellationToken = default);

    Task<WorkOrderDto?> GetAsync(string id, CancellationToken cancellationToken = default);
}
