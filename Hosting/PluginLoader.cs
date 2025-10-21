using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Contracts;

namespace BlazorPdfApp.Hosting;

public sealed class PluginManifest
{
    public required PluginManifest Manifest { get; init; }
    public required string Folder { get; init; }
    public required Assembly Assembly { get; init; }
    public required IPlugin Instance { get; init; }
    public IBlazorPlugin? Blazor => Instance as IBlazorPlugin;
}

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _pluginDir;

    public PluginLoadContext(string pluginDir) : base(isCollectible: false)
    {
        _pluginDir = pluginDir;
        Resolving += OnResolving;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var candidate = Path.Combine(_pluginDir, assemblyName.Name + ".dll");
        if (File.Exists(candidate))
        {
            return LoadFromAssemblyPath(candidate);
        }

        return null;
    }

    private Assembly? OnResolving(AssemblyLoadContext alc, AssemblyName name)
    {
        var candidate = Path.Combine(_pluginDir, name.Name + ".dll");
        return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
    }
}

public static class PluginLoader
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static List<PluginManifest> LoadManifests(string rootDir)
    {
        var result = new List<PluginManifest>();
        if (!Directory.Exists(rootDir))
        {
            return result;
        }

        foreach (var dir in Directory.GetDirectories(rootDir))
        {
            var manifestPath = Path.Combine(dir, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<PluginManifest>(json, ManifestJsonOptions);
                if (manifest is null)
                {
                    continue;
                }

                manifest.Folder = dir;
                result.Add(manifest);
            }
            catch
            {
                // ignore invalid manifest
            }
        }

        return result;
    }

    public static List<PluginManifest> LoadAll(IServiceProvider services, string rootDir)
    {
        var descriptors = new List<PluginManifest>();
        foreach (var manifest in LoadManifests(rootDir))
        {
            if (string.IsNullOrWhiteSpace(manifest.Folder))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(manifest.Assembly) ||
                string.IsNullOrWhiteSpace(manifest.EntryType))
            {
                continue;
            }

            var assemblyPath = Path.Combine(manifest.Folder, manifest.Assembly);
            if (!File.Exists(assemblyPath))
            {
                continue;
            }

            var alc = new PluginLoadContext(manifest.Folder);
            Assembly asm;
            try
            {
                asm = alc.LoadFromAssemblyPath(assemblyPath);
            }
            catch
            {
                continue;
            }

            var entryType = asm.GetType(manifest.EntryType, throwOnError: false, ignoreCase: false);
            if (entryType is null)
            {
                continue;
            }

            if (!typeof(IPlugin).IsAssignableFrom(entryType))
            {
                continue;
            }

            IPlugin? instance;
            try
            {
                instance = (IPlugin?)Activator.CreateInstance(entryType);
                if (instance is null)
                {
                    continue;
                }

                instance.Initialize(services);
            }
            catch
            {
                continue;
            }

            descriptors.Add(new PluginManifest
            {
                Manifest = manifest,
                Folder = manifest.Folder,
                Assembly = asm,
                Instance = instance,
            });
        }

        return descriptors;
    }
}
