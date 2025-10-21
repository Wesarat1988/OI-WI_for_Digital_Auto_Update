using System;
using Contracts;

namespace BlazorPdfApp.Hosting;

public static class PluginMapping
{
    public static PluginManifest ToContract(this PluginDescriptor descriptor)
    {
        if (!descriptor.TryValidate(out var error))
        {
            throw new InvalidOperationException(error ?? "Invalid plugin descriptor.");
        }

        return new PluginManifest
        {
            Id = descriptor.Id!,
            Name = descriptor.Name!,
            Version = descriptor.Version!,
            Assembly = descriptor.Assembly!,
            RouteBase = descriptor.RouteBase,
            EntryType = descriptor.EntryType!
        };
    }
}
