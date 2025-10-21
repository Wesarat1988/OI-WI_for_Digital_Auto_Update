namespace Contracts.WorkOrders;

public sealed record WorkOrderDto(
    string Id,
    string Number,
    string Status,
    string Line,
    string PartNo,
    DateTime CreatedUtc,
    DateTime? DueUtc);
