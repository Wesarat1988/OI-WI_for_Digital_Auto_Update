using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace BlazorPdfApp.Hosting;

internal static class ComponentTypeSanitizer
{
    private static readonly ConcurrentDictionary<Type, Type> Cache = new();
    private static readonly ModuleBuilder Module = CreateModule();

    public static Type? EnsureValid(Type? componentType)
    {
        if (componentType is null)
        {
            return null;
        }

        var name = componentType.Name;
        if (string.IsNullOrEmpty(name) || !char.IsLower(name[0]))
        {
            return componentType;
        }

        return Cache.GetOrAdd(componentType, CreateProxyType);
    }

    private static ModuleBuilder CreateModule()
    {
        var assemblyName = new AssemblyName("BlazorPdfApp.PluginComponentProxies");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        return assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
    }

    private static Type CreateProxyType(Type original)
    {
        var originalName = string.IsNullOrEmpty(original.Name)
            ? "PluginComponent"
            : original.Name!;
        var sanitizedName = char.ToUpperInvariant(originalName[0]) +
            (originalName.Length > 1 ? originalName.Substring(1) : string.Empty);
        if (sanitizedName == originalName)
        {
            sanitizedName = "Proxy" + sanitizedName;
        }

        var ns = string.IsNullOrEmpty(original.Namespace)
            ? "BlazorPdfApp.PluginComponentProxies"
            : original.Namespace;

        var typeBuilder = Module.DefineType(
            $"{ns}.{sanitizedName}Proxy",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
            original);

        typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);
        return typeBuilder.CreateTypeInfo()!;
    }
}
