namespace BlazorPdfApp.Hosting;

public class PluginManifest
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Assembly { get; set; } = string.Empty;

    public string EntryType { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public string Folder { get; set; } = string.Empty;
}
