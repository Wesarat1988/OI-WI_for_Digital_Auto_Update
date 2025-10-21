using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Contracts;

namespace BlazorPdfApp.Hosting;

public sealed class PluginRegistration
{
    public required PluginDescriptor Descriptor { get; init; }
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
    // ====== Cache / Exposed state ================================================================
    private static readonly List<PluginRegistration> _cache = new();
    private static readonly List<Assembly> _loadedAssemblies = new();

    /// <summary>ปลั๊กอินที่ถูกโหลดล่าสุด (อ่านอย่างเดียว)</summary>
    public static IReadOnlyList<PluginRegistration> Registrations => _cache;

    /// <summary>Assemblies ของปลั๊กอินทั้งหมดที่ถูกโหลด (ใช้กับ Router.AdditionalAssemblies ได้)</summary>
    public static IReadOnlyList<Assembly> LoadedAssemblies => _loadedAssemblies;

    // ====== JSON options สำหรับ manifest =========================================================
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static List<PluginDescriptor> LoadDescriptors(string rootDir)
    {
        var result = new List<PluginDescriptor>();
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
                var descriptor = JsonSerializer.Deserialize<PluginDescriptor>(json, ManifestJsonOptions);
                if (descriptor is null)
                {
                    continue;
                }

                descriptor.Folder = dir;
                result.Add(descriptor);
            }
            catch
            {
                // ignore invalid manifest
            }
        }

        return result;
    }

    public static List<PluginManifest> LoadManifests(string rootDir)
    {
        var descriptors = LoadDescriptors(rootDir);
        var manifests = new List<PluginManifest>(descriptors.Count);

        foreach (var descriptor in descriptors)
        {
            if (!descriptor.TryValidate(out _))
            {
                continue;
            }

            manifests.Add(descriptor.ToContract());
        }

        return manifests;
    }

    public static List<PluginRegistration> LoadAll(IServiceProvider services, string rootDir)
    {
        var registrations = new List<PluginRegistration>();
        foreach (var descriptor in LoadDescriptors(rootDir))
        {
            if (!descriptor.TryValidate(out _))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(descriptor.Folder))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(descriptor.Assembly) ||
                string.IsNullOrWhiteSpace(descriptor.EntryType))
            {
                continue;
            }

            var assemblyPath = Path.Combine(descriptor.Folder, descriptor.Assembly);
            if (!File.Exists(assemblyPath))
            {
                continue;
            }

            var alc = new PluginLoadContext(descriptor.Folder);
            Assembly asm;
            try
            {
                asm = alc.LoadFromAssemblyPath(assemblyPath);
            }
            catch
            {
                continue;
            }

            var entryType = ResolveEntryType(asm, descriptor.EntryType);
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
                if (instance is null) continue;

                instance.Initialize(services);

                if (instance is IBlazorPlugin blazorPlugin)
                {
                    instance = BlazorPluginProxy.WrapIfNeeded(blazorPlugin);
                }
            }
            catch
            {
                continue;
            }

            var manifest = descriptor.ToContract();

            registrations.Add(new PluginRegistration
            {
                Descriptor = descriptor,
                Manifest = manifest,
                Folder = descriptor.Folder!,
                Assembly = asm,
                Instance = instance,
            });
        }

        return registrations;
    }

    public static Type? ResolveEntryType(Assembly assembly, string? entryTypeName)
    {
        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        if (string.IsNullOrWhiteSpace(entryTypeName))
        {
            return null;
        }

        var resolved = assembly.GetType(entryTypeName, throwOnError: false, ignoreCase: false);
        if (resolved is not null)
        {
            return resolved;
        }

        return assembly
            .GetTypes()
            .FirstOrDefault(t => string.Equals(t.FullName, entryTypeName, StringComparison.Ordinal) ||
                                 string.Equals(t.Name, entryTypeName, StringComparison.Ordinal));
    }
}
