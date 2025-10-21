using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        CopyCustomAttributes(original, typeBuilder);

        return typeBuilder.CreateTypeInfo()!;
    }

    private static void CopyCustomAttributes(Type source, TypeBuilder destination)
    {
        foreach (var attribute in source.GetCustomAttributesData())
        {
            try
            {
                var ctorArgs = GetConstructorArguments(attribute);
                var (namedProperties, propertyValues, namedFields, fieldValues) = GetNamedArguments(attribute);

                var builder = new CustomAttributeBuilder(
                    attribute.Constructor,
                    ctorArgs,
                    namedProperties,
                    propertyValues,
                    namedFields,
                    fieldValues);

                destination.SetCustomAttribute(builder);
            }
            catch
            {
                // Ignore attributes that cannot be reproduced on the proxy type.
            }
        }
    }

    private static object?[] GetConstructorArguments(CustomAttributeData attribute)
    {
        if (attribute.ConstructorArguments.Count == 0)
        {
            return Array.Empty<object?>();
        }

        var values = new object?[attribute.ConstructorArguments.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = ConvertArgument(attribute.ConstructorArguments[i]);
        }

        return values;
    }

    private static (PropertyInfo[] Properties, object?[] PropertyValues, FieldInfo[] Fields, object?[] FieldValues)
        GetNamedArguments(CustomAttributeData attribute)
    {
        if (attribute.NamedArguments.Count == 0)
        {
            return (Array.Empty<PropertyInfo>(), Array.Empty<object?>(), Array.Empty<FieldInfo>(), Array.Empty<object?>());
        }

        var properties = new List<PropertyInfo>();
        var propertyValues = new List<object?>();
        var fields = new List<FieldInfo>();
        var fieldValues = new List<object?>();

        foreach (var namedArg in attribute.NamedArguments)
        {
            if (namedArg.IsField)
            {
                if (namedArg.MemberInfo is FieldInfo field)
                {
                    fields.Add(field);
                    fieldValues.Add(ConvertArgument(namedArg.TypedValue));
                }
            }
            else if (namedArg.MemberInfo is PropertyInfo property)
            {
                properties.Add(property);
                propertyValues.Add(ConvertArgument(namedArg.TypedValue));
            }
        }

        return (properties.ToArray(), propertyValues.ToArray(), fields.ToArray(), fieldValues.ToArray());
    }

    private static object? ConvertArgument(CustomAttributeTypedArgument argument)
    {
        if (argument.ArgumentType.IsArray)
        {
            if (argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> items)
            {
                var elementType = argument.ArgumentType.GetElementType() ?? typeof(object);
                var array = Array.CreateInstance(elementType, items.Count);

                var index = 0;
                foreach (var item in items)
                {
                    array.SetValue(ConvertArgument(item), index++);
                }

                return array;
            }

            return null;
        }

        return argument.Value;
    }
}
