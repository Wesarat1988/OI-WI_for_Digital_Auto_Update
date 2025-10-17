using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Contracts; // ถ้าโปรเจกต์นี้อ้างอิง Contracts อยู่แล้ว

namespace BlazorPdfApp.Hosting;

public static class PluginLoader
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static List<PluginManifest> LoadManifests(string rootDir)
    {
        var manifests = new List<PluginManifest>();
        if (!Directory.Exists(rootDir))
        {
            return manifests;
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
                if (manifest is not null)
                {
                    manifests.Add(manifest);
                }
            }
            catch
            {
                // ignore invalid json
            }
        }

        return manifests;
    }

    public static IReadOnlyList<IPlugin> LoadAll(IServiceProvider services, string pluginsDir)
    {
        var list = new List<IPlugin>();
        if (!Directory.Exists(pluginsDir)) return list;

        foreach (var dir in Directory.GetDirectories(pluginsDir))
        {
            var manifestPath = Path.Combine(dir, "plugin.json");
            if (!File.Exists(manifestPath)) continue;

            var manifest = JsonSerializer.Deserialize<PluginManifest>(
                File.ReadAllText(manifestPath),
                ManifestJsonOptions
            );
            if (manifest is null) continue;

            if (string.IsNullOrWhiteSpace(manifest.Assembly) ||
                string.IsNullOrWhiteSpace(manifest.EntryType))
            {
                continue;
            }

            var asmPath = Path.Combine(dir, manifest.Assembly);
            if (!File.Exists(asmPath)) continue;

            var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(asmPath);
            var type = asm.GetType(manifest.EntryType, throwOnError: true)!;
            var plug = (IPlugin)Activator.CreateInstance(type)!;

            plug.Initialize(services);
            list.Add(plug);
        }

        return list;
    }
}
