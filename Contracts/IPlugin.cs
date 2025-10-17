namespace Contracts;

using System;
using System.Threading.Tasks;

public interface IPlugin
{
    string Id { get; }

    string Name { get; }

    string Version { get; }

    void Initialize(IServiceProvider services);

    Task ExecuteAsync();
}

public interface IBlazorPlugin : IPlugin
{
    Type? RootComponent { get; }
}
