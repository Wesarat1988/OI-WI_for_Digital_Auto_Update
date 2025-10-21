using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Contracts;

namespace BlazorPdfApp.Hosting;

internal static class BlazorPluginProxy
{
    public static Contracts.IBlazorPlugin Sanitize(Contracts.IBlazorPlugin plugin)
    {
        if (plugin is null)
        {
            throw new ArgumentNullException(nameof(plugin));
        }

        if (plugin is DispatchProxy)
        {
            return plugin;
        }

        // Trigger creation (and caching) of a sanitized component type if necessary.
        _ = ComponentTypeSanitizer.EnsureValid(plugin.RootComponent);

        var proxy = DispatchProxy.Create<Contracts.IBlazorPlugin, SanitizingProxy>();
        if (proxy is null)
        {
            return plugin;
        }

        ((SanitizingProxy)(object)proxy).Initialize(plugin);
        return proxy;
    }

    private sealed class SanitizingProxy : DispatchProxy
    {
        private Contracts.IBlazorPlugin _inner = default!;

        public void Initialize(Contracts.IBlazorPlugin inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            var result = targetMethod.Invoke(_inner, args);
            if (result is null)
            {
                return null;
            }

            var returnType = targetMethod.ReturnType;
            if (returnType == typeof(Type) && result is Type componentType)
            {
                return SanitizeType(componentType);
            }

            if (typeof(IEnumerable<Type>).IsAssignableFrom(returnType) && result is IEnumerable<Type> types)
            {
                var sanitized = types
                    .Select(t => SanitizeType(t) ?? t)
                    .ToArray();

                if (returnType.IsArray)
                {
                    return sanitized;
                }

                if (returnType.IsAssignableFrom(sanitized.GetType()))
                {
                    return sanitized;
                }

                return sanitized;
            }

            return result;
        }

        private static Type? SanitizeType(Type? type)
        {
            if (type is null)
            {
                return null;
            }

            return ComponentTypeSanitizer.EnsureValid(type) ?? type;
        }
    }
}
