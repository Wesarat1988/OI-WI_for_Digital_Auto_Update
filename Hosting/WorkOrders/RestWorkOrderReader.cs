using System.Net;
using System.Net.Http.Json;
using Contracts.Common;
using Contracts.WorkOrders;
using Microsoft.Extensions.Logging;

namespace BlazorPdfApp.WorkOrders;

public sealed class RestWorkOrderReader : IWorkOrderReader
{
    internal const string ClientName = "WorkOrders";
    private readonly IHttpClientFactory _clientFactory;
    private readonly ILogger<RestWorkOrderReader> _logger;

    public RestWorkOrderReader(IHttpClientFactory clientFactory, ILogger<RestWorkOrderReader> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public Task<PageResult<WorkOrderDto>> SearchAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        if (request.PageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.PageSize), "Page size must be greater than zero.");
        }

        var page = request.Page > 0 ? request.Page : 1;
        var query = BuildQueryString("api/workorders", request with { Page = page });

        return SendWithRetry(
            async (client, token) =>
            {
                using var response = await client.GetAsync(query, token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return new PageResult<WorkOrderDto>(Array.Empty<WorkOrderDto>(), page, request.PageSize, 0);
                }

                response.EnsureSuccessStatusCode();
                var payload = await response.Content
                    .ReadFromJsonAsync<PageResult<WorkOrderDto>>(cancellationToken: token)
                    .ConfigureAwait(false);

                return payload ?? new PageResult<WorkOrderDto>(Array.Empty<WorkOrderDto>(), page, request.PageSize, 0);
            },
            cancellationToken,
            () => new PageResult<WorkOrderDto>(Array.Empty<WorkOrderDto>(), page, request.PageSize, 0));
    }

    public Task<WorkOrderDto?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));
        }

        var resource = $"api/workorders/{Uri.EscapeDataString(id)}";

        return SendWithRetry(
            async (client, token) =>
            {
                using var response = await client.GetAsync(resource, token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<WorkOrderDto>(cancellationToken: token).ConfigureAwait(false);
            },
            cancellationToken,
            () => null);
    }

    private async Task<T> SendWithRetry<T>(Func<HttpClient, CancellationToken, Task<T>> operation, CancellationToken cancellationToken, Func<T> fallback)
    {
        const int maxAttempts = 3;
        var delay = TimeSpan.FromMilliseconds(200);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var client = _clientFactory.CreateClient(ClientName);
                return await operation(client, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                if (attempt >= maxAttempts)
                {
                    _logger.LogError(ex, "Work order REST request failed after {Attempts} attempts.", attempt);
                    break;
                }

                _logger.LogWarning(ex, "Work order REST request failed (attempt {Attempt}/{MaxAttempts}). Retrying...", attempt, maxAttempts);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay += delay; // Exponential back-off
            }
        }

        return fallback();
    }

    private static string BuildQueryString(string path, PageRequest request)
    {
        var parameters = new List<string>
        {
            $"page={request.Page}",
            $"pageSize={request.PageSize}"
        };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            parameters.Add($"search={Uri.EscapeDataString(request.Search!)}");
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            parameters.Add($"status={Uri.EscapeDataString(request.Status!)}");
        }

        if (!string.IsNullOrWhiteSpace(request.Line))
        {
            parameters.Add($"line={Uri.EscapeDataString(request.Line!)}");
        }

        if (!string.IsNullOrWhiteSpace(request.PartNo))
        {
            parameters.Add($"partNo={Uri.EscapeDataString(request.PartNo!)}");
        }

        if (request.FromUtc is not null)
        {
            parameters.Add($"fromUtc={Uri.EscapeDataString(request.FromUtc.Value.ToString("O"))}");
        }

        if (request.ToUtc is not null)
        {
            parameters.Add($"toUtc={Uri.EscapeDataString(request.ToUtc.Value.ToString("O"))}");
        }

        return parameters.Count == 0
            ? path
            : $"{path}?{string.Join("&", parameters)}";
    }
}
