namespace BlazorPdfApp.Hosting;

public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    string Assembly,
    string EntryType
);
