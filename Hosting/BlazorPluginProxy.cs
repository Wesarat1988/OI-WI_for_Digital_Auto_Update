using System;
using System.Reflection;

namespace BlazorPdfApp.Hosting;

internal static class BlazorPluginProxy
{
    public static IBlazorPlugin Sanitize(IBlazorPlugin plugin)
    {
        if (plugin is null)
        {
            throw new ArgumentNullException(nameof(plugin));
        }

        var sanitizedType = ComponentTypeSanitizer.EnsureValid(plugin.RootComponent);
        if (sanitizedType is null || sanitizedType == plugin.RootComponent)
        {
            return plugin;
        }

        var proxy = DispatchProxy.Create<IBlazorPlugin, SanitizingProxy>();
        if (proxy is null)
        {
            return plugin;
        }

        ((SanitizingProxy)(object)proxy).Initialize(plugin, sanitizedType);
        return proxy;
    }

    private sealed class SanitizingProxy : DispatchProxy
    {
        private IBlazorPlugin _inner = default!;
        private Type? _sanitized;

        public void Initialize(IBlazorPlugin inner, Type sanitized)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _sanitized = sanitized ?? throw new ArgumentNullException(nameof(sanitized));
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            if (targetMethod.Name == "get_RootComponent")
            {
                return _sanitized ?? _inner.RootComponent;
            }

            return targetMethod.Invoke(_inner, args);
        }
    }
}
