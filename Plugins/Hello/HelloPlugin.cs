using System;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelloPlugin;

public sealed class HelloPlugin : IBlazorPlugin
{
    private ILogger<HelloPlugin>? _logger;

    public string Id => "demo.hello";

    public string Name => "Hello Plugin";

    public string Version => "1.0.0";

    public Type? RootComponent => typeof(Pages.HelloPanel);

    public string? RouteBase => "/plugins/hello";

    public void Initialize(IServiceProvider services)
    {
        _logger = services.GetService<ILogger<HelloPlugin>>();
        _logger?.LogInformation("HelloPlugin initialized");
    }

    public Task ExecuteAsync(CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        _logger?.LogInformation("HelloPlugin ExecuteAsync invoked");
        return Task.CompletedTask;
    }

    Task IPlugin.ExecuteAsync() => ExecuteAsync();
}
