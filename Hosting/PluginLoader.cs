using System.IO;
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

    // ====== อ่านไฟล์ plugin.json เป็น Descriptor (ของ Host) =====================================
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
                if (descriptor is null) continue;

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

    // ====== แปลง Descriptor → Contracts.Manifest (ไม่แตะ Contracts.csproj) =======================
    public static List<PluginManifest> LoadManifests(string rootDir)
    {
        var descriptors = LoadDescriptors(rootDir);
        var manifests = new List<PluginManifest>(descriptors.Count);

        foreach (var descriptor in descriptors)
        {
            if (!descriptor.TryValidate(out _)) continue;
            manifests.Add(descriptor.ToContract());
        }

        return manifests;
    }

    // ====== โหลดปลั๊กอินทั้งหมด + เติม Cache =====================================================
    public static List<PluginRegistration> LoadAll(IServiceProvider services, string rootDir)
    {
        var registrations = new List<PluginRegistration>();

        foreach (var descriptor in LoadDescriptors(rootDir))
        {
            if (!descriptor.TryValidate(out _)) continue;
            if (string.IsNullOrWhiteSpace(descriptor.Folder)) continue;
            if (string.IsNullOrWhiteSpace(descriptor.Assembly) ||
                string.IsNullOrWhiteSpace(descriptor.EntryType)) continue;

            var assemblyPath = Path.Combine(descriptor.Folder, descriptor.Assembly);
            if (!File.Exists(assemblyPath)) continue;

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

            var entryType = asm.GetType(descriptor.EntryType, throwOnError: false, ignoreCase: false);
            if (entryType is null) continue;
            if (!typeof(IPlugin).IsAssignableFrom(entryType)) continue;

            IPlugin? instance;
            try
            {
                instance = (IPlugin?)Activator.CreateInstance(entryType);
                if (instance is null) continue;

                instance.Initialize(services);
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

        // ---- update central cache (atomic replace) ----
        _cache.Clear();
        _cache.AddRange(registrations);

        _loadedAssemblies.Clear();
        _loadedAssemblies.AddRange(registrations.Select(r => r.Assembly));

        return registrations;
    }

    // ====== Helper: หา Type ของคอมโพเนนต์ Entry เพื่อเรนเดอร์ด้วย DynamicComponent ============
    /// <summary>
    /// คืนค่า Type ของคอมโพเนนต์รากสำหรับปลั๊กอินตาม id (เช่น "workorder") จากปลั๊กอินที่ถูกโหลดไว้แล้ว
    /// </summary>
    public static Type? ResolveEntryType(string id)
    {
        // หา registration จาก id (ลองทั้ง Descriptor.Id และ Manifest.Id)
        var reg = _cache.FirstOrDefault(r =>
            string.Equals(r.Descriptor.Id, id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));

        if (reg is null) return null;

        // 1) ถ้าเป็น IBlazorPlugin และมี property บอก root component type ให้ใช้ก่อน
        if (reg.Blazor is not null)
        {
            var bt = TryGetRootTypeFromBlazorPlugin(reg.Blazor);
            if (bt is not null) return bt;
        }

        // 2) ใช้ entryType แบบ fully-qualified ที่มาจาก descriptor/manifest
        var typeName = reg.Descriptor.EntryType;
        if (!string.IsNullOrWhiteSpace(typeName))
        {
            var t = reg.Assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (t is not null) return t;
        }

        return null;
    }

    private static Type? TryGetRootTypeFromBlazorPlugin(IBlazorPlugin blazorPlugin)
    {
        // ใช้รีเฟลกชันหา property ที่มักใช้กัน (ไม่ผูกกับคอนแทรกต์ใด ๆ)
        var t = blazorPlugin.GetType();
        var p =
            t.GetProperty("RootComponentType") ??
            t.GetProperty("RootComponent") ??
            t.GetProperty("RootType");

        return p?.GetValue(blazorPlugin) as Type;
    }
}
