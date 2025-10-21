namespace Contracts.Common;

public sealed record PageRequest(
    int Page,
    int PageSize,
    string? Search = null,
    string? Status = null,
    string? Line = null,
    string? PartNo = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);

public sealed record PageResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);
