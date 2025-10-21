using System;
using System.Reflection;
using Contracts;

namespace BlazorPdfApp.Hosting;

internal static class BlazorPluginProxy
{
    public static Contracts.IBlazorPlugin WrapIfNeeded(Contracts.IBlazorPlugin plugin)
    {
        var sanitizedType = ComponentTypeSanitizer.EnsureValid(plugin.RootComponent);
        if (sanitizedType == null || sanitizedType == plugin.RootComponent)
        {
            return plugin;
        }

        var proxy = DispatchProxy.Create<Contracts.IBlazorPlugin, SanitizingProxy>();
        ((SanitizingProxy)proxy!).Initialize(plugin, sanitizedType);
        return (Contracts.IBlazorPlugin)proxy!;
    }

    private sealed class SanitizingProxy : DispatchProxy
    {
        private Contracts.IBlazorPlugin? _inner;
        private Type? _sanitized;

        public void Initialize(Contracts.IBlazorPlugin inner, Type sanitized)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _sanitized = sanitized;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new ArgumentNullException(nameof(targetMethod));
            }

            if (_inner is null)
            {
                throw new InvalidOperationException("Proxy not initialized.");
            }

            if (targetMethod.Name == "get_RootComponent")
            {
                return _sanitized;
            }

            if (targetMethod.Name == "set_RootComponent" && args is { Length: 1 })
            {
                var provided = args[0] as Type;
                var sanitized = ComponentTypeSanitizer.EnsureValid(provided);
                _sanitized = sanitized ?? provided;
                args[0] = _sanitized;
            }

            return targetMethod.Invoke(_inner, args);
        }
    }
}
