namespace BlazorPdfApp.WorkOrders;

public enum WorkOrderSource
{
    SqlServer,
    RestApi
}

public sealed class WorkOrderReaderOptions
{
    public WorkOrderSource Source { get; set; } = WorkOrderSource.SqlServer;

    public string? ConnectionString { get; set; }

    public string? ApiBaseUrl { get; set; }

    public string? ApiKey { get; set; }
}
